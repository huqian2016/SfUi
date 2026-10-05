#!/bin/bash
# SfUi (Avalonia) macOS 用 .app バンドル作成スクリプト（macOS 上で実行）
#
# 使い方:
#   bash packaging/make-mac-app.sh [osx-arm64|osx-x64]
#
# 環境変数（任意・署名 / 公証する場合のみ）:
#   SFUI_CODESIGN_IDENTITY   Developer ID Application 証明書の名前
#   SFUI_NOTARIZE_APPLE_ID   Apple ID
#   SFUI_NOTARIZE_TEAM_ID    Team ID
#   SFUI_NOTARIZE_PASSWORD   App 用パスワード
set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(grep -o '<Version>[^<]*' "$ROOT/src/SfUi.Avalonia/SfUi.Avalonia.csproj" | sed 's/<Version>//' | head -1)"
VERSION="${VERSION:-0.0.0}"
PUBLISH="$ROOT/dist/mac/$RID/publish"
APP="$ROOT/dist/mac/SfUi.app"

echo "== SfUi (Avalonia) macOS パッケージ: version=$VERSION rid=$RID =="

rm -rf "$PUBLISH" "$APP"
dotnet publish "$ROOT/src/SfUi.Avalonia/SfUi.Avalonia.csproj" \
    -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=false -o "$PUBLISH"

# ---- .app 構造 ----
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH"/. "$APP/Contents/MacOS/"

# ---- アイコン（packaging/mac/SfUi.iconset の PNG 一式 → SfUi.icns。iconutil が無い環境ではスキップ） ----
# iconset の PNG は SfUi.ico の 256px フレームから生成済み（コミット済みなので macOS 以外でも同じ絵になる）。
ICONSET="$ROOT/packaging/mac/SfUi.iconset"
if command -v iconutil >/dev/null 2>&1 && [ -d "$ICONSET" ]; then
    iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/SfUi.icns" 2>/dev/null || true
fi

# ---- Info.plist（バージョン埋め込み） ----
sed "s/__VERSION__/$VERSION/g" "$ROOT/packaging/mac/Info.plist" > "$APP/Contents/Info.plist"

# ---- 任意: 署名 / 公証 ----
if [ -n "${SFUI_CODESIGN_IDENTITY:-}" ]; then
    echo "== codesign: $SFUI_CODESIGN_IDENTITY =="
    ENTITLEMENTS="$ROOT/packaging/mac/entitlements.plist"
    if [ -f "$ENTITLEMENTS" ]; then
        codesign --force --deep --options runtime --entitlements "$ENTITLEMENTS" --sign "$SFUI_CODESIGN_IDENTITY" "$APP"
    else
        codesign --force --deep --options runtime --sign "$SFUI_CODESIGN_IDENTITY" "$APP"
    fi
    codesign --verify --deep --strict --verbose=2 "$APP"
fi

if [ -n "${SFUI_NOTARIZE_APPLE_ID:-}" ] && [ -n "${SFUI_NOTARIZE_TEAM_ID:-}" ] && [ -n "${SFUI_NOTARIZE_PASSWORD:-}" ]; then
    echo "== notarize =="
    NOTARIZE_ZIP="$ROOT/dist/mac/SfUi-notarize.zip"
    rm -f "$NOTARIZE_ZIP"
    ditto -c -k --keepParent "$APP" "$NOTARIZE_ZIP"
    xcrun notarytool submit "$NOTARIZE_ZIP" \
        --apple-id "$SFUI_NOTARIZE_APPLE_ID" \
        --team-id "$SFUI_NOTARIZE_TEAM_ID" \
        --password "$SFUI_NOTARIZE_PASSWORD" --wait
    xcrun stapler staple "$APP"
    rm -f "$NOTARIZE_ZIP"
fi

# ---- 配布用 zip ----
ZIP="$ROOT/dist/mac/SfUi-$VERSION-$RID.zip"
rm -f "$ZIP"
(cd "$ROOT/dist/mac" && ditto -c -k --keepParent "SfUi.app" "$ZIP")

echo "OK:  $APP"
echo "ZIP: $ZIP"
echo "ヒント: 起動は 'open dist/mac/SfUi.app'。データは exe(バンドル)隣接、書込不可時は ~/Library/Application Support/SfUi にフォールバックします。"
