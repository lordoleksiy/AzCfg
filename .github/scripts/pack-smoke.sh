#!/usr/bin/env bash
# Pack smoke test (ADR 0016): the packed `azcfg` tool must carry the UI and its static web assets.
# Usage: pack-smoke.sh [package-dir]   (without an argument the Cli project is packed into a temp dir)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"
port=5199
pid=""

cleanup() {
  [ -n "$pid" ] && kill "$pid" 2>/dev/null || true
  sleep 1
  rm -rf "$work" 2>/dev/null || true
}
trap cleanup EXIT

fail() { echo "pack-smoke: FAIL - $*" >&2; [ -f "$work/out.txt" ] && sed 's/^/  out: /' "$work/out.txt" >&2; [ -f "$work/err.txt" ] && sed 's/^/  err: /' "$work/err.txt" >&2; exit 1; }

pkgdir="${1:-}"
if [ -z "$pkgdir" ]; then
  pkgdir="$work/pkg"
  dotnet pack "$root/src/AzCfg.Cli" -c Release -o "$pkgdir"
fi

nupkg="$(ls "$pkgdir"/AzCfg.*.nupkg | head -n 1)"
version="$(basename "$nupkg" .nupkg)"
version="${version#AzCfg.}"
echo "pack-smoke: installing AzCfg $version"
dotnet tool install --tool-path "$work/tool" --add-source "$pkgdir" AzCfg --version "$version"

azcfg="$work/tool/azcfg"
[ -x "$azcfg" ] || azcfg="$work/tool/azcfg.exe"

# Run from an unrelated directory: assets must resolve from the install dir, not the cwd.
mkdir "$work/cwd"
( cd "$work/cwd" && exec "$azcfg" ui --demo --no-browser --json --port "$port" >"$work/out.txt" 2>"$work/err.txt" ) &
pid=$!

url=""
for _ in $(seq 1 60); do
  url="$(sed -nE 's/.*"url":"([^"]+)".*/\1/p' "$work/out.txt" 2>/dev/null | head -n 1)"
  [ -n "$url" ] && break
  kill -0 "$pid" 2>/dev/null || fail "azcfg ui exited before printing the url"
  sleep 1
done
[ -n "$url" ] || fail "no url printed within 60s"
echo "pack-smoke: url $url"

base="${url%%/login*}"

code="$(curl -s -o /dev/null -w '%{http_code}' "$url")"
[ "$code" = "302" ] || fail "GET /login?t= expected 302, got $code"

# Own static asset and one asset of each third-party library, taken from the installed package.
wwwroot="$(find "$work/tool/.store" -type d -name wwwroot | head -n 1)"
[ -n "$wwwroot" ] || fail "installed tool has no wwwroot"
for lib in Microsoft.FluentUI.AspNetCore.Components BlazorMonaco; do
  asset="$(find "$wwwroot/_content/$lib" -type f \( -name '*.js' -o -name '*.css' \) | head -n 1)"
  [ -n "$asset" ] || fail "package has no _content/$lib assets"
  rel="${asset#"$wwwroot"/}"
  code="$(curl -s -o /dev/null -w '%{http_code}' "$base/$rel")"
  [ "$code" = "200" ] || fail "GET /$rel expected 200, got $code"
  echo "pack-smoke: /$rel -> 200"
done

echo "pack-smoke: OK"
