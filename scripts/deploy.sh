#!/usr/bin/env bash
# Build the site and publish it to Cloudflare Pages.
#
# Needs two settings in the shell before it runs:
#   CLOUDFLARE_API_TOKEN   the Cloudflare token (never save it in this repo)
#   CLOUDFLARE_ACCOUNT_ID  the Cloudflare account ID
#
# Optional: DEPLOY_BRANCH=preview uploads a private test copy and leaves
# the real site alone. Add PREVIEW_FX=1 to show the effects panel on it.
#
# The upload is done from a folder outside the repo, because wrangler
# writes its own settings files wherever it runs. Only the built site
# (_site) is uploaded. The onrecord folder is never part of it.
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
work="${DEPLOY_DIR:-${TMPDIR:-/tmp}/globalmayhem-deploy}"

cd "$repo"
rm -rf _site
npx eleventy

rm -rf "$work"
mkdir -p "$work"
cp -r _site "$work/site"

cd "$work"
CI=1 npx -y wrangler@latest pages deploy site --project-name globalmayhem --branch "${DEPLOY_BRANCH:-main}" --commit-dirty=true
