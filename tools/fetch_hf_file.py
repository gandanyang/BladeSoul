#!/usr/bin/env python3
"""分块并行下载 HuggingFace 大文件（走本地代理）。

背景：HF 的 LFS CDN（cdn-lfs.huggingface.co）单连接在国内约 300KB/s，
但多连接并行可聚合到 ~7MB/s。curl 自身不带多连接，本项目又不依赖
aria2c，因此用本脚本以 HTTP Range 分块 + 线程池并行拉取，最后按序拼装。

用法：
    python tools/fetch_hf_file.py \
        --url https://huggingface.co/Comfy-Org/hunyuan3D_2.1_repackaged/resolve/main/hunyuan_3d_v2.1.safetensors \
        --out "F:\\ComfyUI-aki-v3\\ComfyUI\\models\\checkpoints\\hunyuan_3d_v2.1.safetensors" \
        --proxy http://127.0.0.1:7897 --workers 8 --chunk-mb 256

结束后校验总字节数；失败分块会重试，重试仍失败则整体以非零码退出。
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor, as_completed

CURL = "curl.exe" if os.name == "nt" else "curl"
MB = 1024 * 1024


def head_size(url: str, proxy: str | None, retries: int = 3) -> int:
    """取远端文件总字节数（用 Range 探测而不是 HEAD，避免 CDN 不返回长度）。"""
    last = ""
    for i in range(retries):
        cmd = [CURL, "-sS", "-L", "--location-trusted", "-o", os.devnull,
               "-r", "0-0", "-D", "-", url]
        if proxy:
            cmd += ["--proxy", proxy]
        try:
            out = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
        except subprocess.TimeoutExpired:
            last = "timeout"
            continue
        if out.returncode != 0:
            last = out.stderr.strip()
            continue
        total = 0
        for line in out.stdout.splitlines():
            low = line.lower()
            if low.startswith("content-range:"):
                body = line.split("/", 1)[-1].strip()
                if body.isdigit():
                    total = int(body)
        if total:
            return total
        last = "no content-range in response"
    raise RuntimeError(f"无法获取远端大小：{last}")


def fetch_range(url: str, proxy: str | None, start: int, end: int,
                path: str, retries: int = 5) -> int:
    """下载 [start, end] 闭区间到 path，返回落盘字节数。"""
    expect = end - start + 1
    for attempt in range(1, retries + 1):
        cmd = [CURL, "-sS", "-L", "--location-trusted", "--fail",
               "-r", f"{start}-{end}", "-o", path, url]
        if proxy:
            cmd += ["--proxy", proxy]
        try:
            out = subprocess.run(cmd, capture_output=True, text=True, timeout=1800)
        except subprocess.TimeoutExpired:
            out = None
        if out is not None and out.returncode == 0 and os.path.exists(path):
            got = os.path.getsize(path)
            if got == expect:
                return got
        if attempt < retries:
            time.sleep(2 * attempt)
    raise RuntimeError(f"分块 {start}-{end} 下载失败（期望 {expect} 字节）")


def merge(parts: list[str], out: str) -> None:
    tmp = out + ".tmp"
    with open(tmp, "wb") as fh:
        for p in parts:
            with open(p, "rb") as src:
                while True:
                    buf = src.read(8 * MB)
                    if not buf:
                        break
                    fh.write(buf)
    os.replace(tmp, out)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--proxy", default=None)
    ap.add_argument("--workers", type=int, default=8)
    ap.add_argument("--chunk-mb", type=int, default=256)
    ap.add_argument("--job-name", default=None)
    args = ap.parse_args()

    job = args.job_name or os.path.basename(args.out) + ".job"
    jobdir = os.path.join(os.path.dirname(os.path.abspath(args.out)), job)
    os.makedirs(jobdir, exist_ok=True)

    total = head_size(args.url, args.proxy)
    print(f"远端大小 {total} 字节 ({total / 1024 ** 3:.2f} GiB)，分块 {args.chunk_mb}MB，{args.workers} 路", flush=True)

    chunk = args.chunk_mb * MB
    spans = []
    start = 0
    while start < total:
        end = min(start + chunk - 1, total - 1)
        spans.append((start, end))
        start = end + 1

    parts = [os.path.join(jobdir, f"part_{i:05d}") for i in range(len(spans))]
    done = 0
    lock = threading.Lock()
    t0 = time.time()

    def worker(idx: int) -> None:
        nonlocal done
        s, e = spans[idx]
        if os.path.exists(parts[idx]) and os.path.getsize(parts[idx]) == e - s + 1:
            with lock:
                done += e - s + 1
            return
        got = fetch_range(args.url, args.proxy, s, e, parts[idx])
        with lock:
            done += got
            spd = done / max(time.time() - t0, 1e-6) / MB
            print(f"\r进度 {done * 100 / total:5.1f}%  ({done / 1024 ** 3:.2f}/{total / 1024 ** 3:.2f} GiB)  {spd:5.2f} MB/s",
                  end="", flush=True)

    with ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = [pool.submit(worker, i) for i in range(len(spans))]
        for fut in as_completed(futures):
            fut.result()  # 有分块失败则抛出

    print(flush=True)
    merge(parts, args.out)
    size = os.path.getsize(args.out)
    if size != total:
        print(f"校验失败：落盘 {size} != 期望 {total}", file=sys.stderr)
        return 1
    for p in parts:
        os.remove(p)
    os.rmdir(jobdir)
    print(f"完成：{args.out}  用时 {time.time() - t0:.0f}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
