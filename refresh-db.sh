#!/usr/bin/bash

git pull
(cd scraper; dotnet run)
rsync --out-format="Updated %f (%bB)" -rc scraper/db/ website/www/db/
