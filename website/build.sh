#!/usr/bin/bash
# Type-checks the website and bundles it into www/js/site.js
set -e
cd "$(dirname "$0")"

tsc
esbuild Site.ts --bundle --format=esm --target=es2022 --minify --sourcemap --outfile=www/js/site.js
