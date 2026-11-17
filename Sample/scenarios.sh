#!/usr/bin/env bash
# Runs the main scenarios of register_device (UC-01 and UC-02) one after another. Start from zero: ./down.sh --reset && ./up.sh
cd "$(dirname "$0")"
run() { echo; echo "### $1"; shift; ./call.sh "$@"; }
run "UC-01: register with the required fields" '{"brand":"Shelly","model":"Dimmer 2","manuals":["shelly/dimmer_2_guide.pdf"]}'
run "UC-01: optional fields (type, location, status, date, URL)" '{"brand":"Fibaro","model":"Double Smart","manuals":["fibaro/fgs_2x3_en_manual.pdf"],"device_type":"Switch","location":"Salon","status":"active","purchase_date":"2025-03-01","urls":["https://example.com/support"]}'
run "UC-02: same device written differently (device_exists)" '{"brand":"shelly","model":" dimmer  2 ","manuals":["shelly/rgbw2_app_guide.pdf"]}'
run "UC-02: manual that belongs to another device (manual_in_use)" '{"brand":"Shelly","model":"RGBW2","manuals":["shelly/dimmer_2_guide.pdf"]}'
run "UC-02: manual that does not exist (manual_not_found)" '{"brand":"Shelly","model":"RGBW2","manuals":["shelly/missing.pdf"]}'
run "UC-02: paths outside the folder (manual_not_allowed)" '{"brand":"X","model":"Y","manuals":["../secret.pdf","/etc/passwd","~/a.pdf","%2e%2e/a.pdf"]}'
run "UC-02: required fields missing (invalid_fields)" '{"brand":" ","manuals":[]}'
run "UC-02: no device at all" 'null'
run "UC-01: a valid one that is still free" '{"brand":"Shelly","model":"RGBW2","manuals":["shelly/rgbw2_app_guide.pdf"]}'
echo; echo "### Stored:"; ./db.sh "SELECT device_id, brand, model, location FROM devices; SELECT device_id, manual, COUNT(*) AS chunks FROM chunks GROUP BY device_id, manual;"
