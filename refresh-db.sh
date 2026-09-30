#!/usr/bin/bash

git pull
website/build.sh
(cd scraper; dotnet run)
rsync --out-format="Updated %f (%bB)" -rc scraper/db/ website/www/db/
