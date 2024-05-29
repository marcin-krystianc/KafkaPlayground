@echo off
:my-start

for /l %%x in (2, 1, 3) do (
 echo %%x
 docker stop kafka-%%x
 timeout /t 30
 docker start kafka-%%x
 timeout /t 30
  
REM docker exec -it kafka-1 /bin/bash

)

goto my-start

REM if %ERRORLEVEL% GEQ 1 exit /b %ERRORLEVEL%