#!/usr/bin/bash

if [ $# -lt 2 ]
then
	echo "Usage: $0 [grimoire URL] [output directory]"
	echo "Clones a live grimoire instance's database"
	exit 1
fi

URL="$1"
OUT="$2"

mkdir -p "$OUT"
wget -nv "$URL/db/index.json" -O "$OUT/index.json"

cat "$OUT/index.json" | jq | grep -Po '(?<=").*(?=":)' | while read i
do
	wget -nv "$URL/db/$i.json" -O "$OUT/$i.json"
done
