#!/usr/bin/bash

git pull
(cd website; tsc)
(cd scraper; dotnet run)
rsync --out-format="Updated %f (%bB)" -rc scraper/db/ website/www/db/
