#!/usr/bin/bash

git pull
website/build.sh
(cd scraper; dotnet run)
rsync --out-format="Updated %f (%bB)" --delete -rc scraper/db/ website/www/db/
