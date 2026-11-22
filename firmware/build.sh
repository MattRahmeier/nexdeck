#!/bin/sh
# Builds NEXDECK sketches with arduino-cli (ESP32 core 3.3.x) and optionally flashes over USB.
#   sh firmware/build.sh                          build the launcher and the templates
#   sh firmware/build.sh launcher COM3            build and flash the NEXLauncher (partition app0)
#   sh firmware/build.sh templates/hello_app      build one sketch; the .bin goes to firmware/build/<name>/
#   sh firmware/build.sh path/to/my_app           build your own app (folder name = .ino name)
# Apps go to the SD card as /apps/<name>.bin: tools/make_nexapp.py + NEXDECK HUB, or tools/push.ps1.
set -e
cd "$(dirname "$0")"
CLI=${ARDUINO_CLI:-arduino-cli}
FQBN="esp32:esp32:esp32s3:PSRAM=opi,FlashSize=16M,PartitionScheme=app3M_fat9M_16MB,CDCOnBoot=default,UploadMode=default"
SKETCHES=${1:-"launcher templates/hello_app templates/hello_pc"}
for s in $SKETCHES; do
  name=$(basename "$s")
  echo "== $name"
  "$CLI" compile --fqbn "$FQBN" --libraries libraries --output-dir "build/$name" "$s"
done
if [ -n "$2" ]; then
  "$CLI" upload --fqbn "$FQBN" -p "$2" --input-dir "build/$(basename "$1")" "$1"
fi
