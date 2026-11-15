#!/bin/sh
# Fetch what the camera page (detect.html) needs and is not kept in this repo:
# the two detectors from their own repositories, and onnxruntime-web 1.20.1 (MIT) from npm.
set -e
cd "$(dirname "$0")"
mkdir -p model vendor/ort
for f in fire_smoke_640 fire_smoke_416; do
  curl -fL -o "model/$f.onnx" "https://github.com/qudah269-ops/jamra-fire-smoke/raw/main/src/jamra_fire_smoke/models/$f.onnx"
done
for f in person_640 person_416; do
  curl -fL -o "model/$f.onnx" "https://github.com/qudah269-ops/jamra-life-lock/raw/main/src/jamra_life_lock/models/$f.onnx"
done
for f in ort.min.js ort-wasm-simd-threaded.mjs ort-wasm-simd-threaded.wasm ort-wasm-simd-threaded.jsep.mjs ort-wasm-simd-threaded.jsep.wasm; do
  curl -fL -o "vendor/ort/$f" "https://cdn.jsdelivr.net/npm/onnxruntime-web@1.20.1/dist/$f"
done
echo "done: web/model and web/vendor/ort"
