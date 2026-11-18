#!/usr/bin/env bash
# Runs the main scenarios of register_device and find_devices (UC-01, UC-02 and UC-05 to UC-07) one after another. Start from zero: ./down.sh --reset && ./up.sh
cd "$(dirname "$0")"
run() { echo; echo "### $1"; if [ "${3:-}" = find ]; then ./find.sh "$2"; else ./call.sh "$2"; fi; }
run "UC-07: no device is registered yet (message says to register one)" '{"brand":"Shelly"}' find
run "UC-01: register with the required fields" '{"brand":"Shelly","model":"Dimmer 2","manuals":["shelly/dimmer_2_guide.pdf"]}'
run "UC-01: optional fields (type, location, status, date, URL)" '{"brand":"Fibaro","model":"Double Smart","manuals":["fibaro/fgs_2x3_en_manual.pdf"],"device_type":"Switch","location":"Salon","status":"active","purchase_date":"2025-03-01","urls":["https://example.com/support"]}'
run "UC-02: same device written differently (device_exists)" '{"brand":"shelly","model":" dimmer  2 ","manuals":["shelly/rgbw2_app_guide.pdf"]}'
run "UC-02: manual that belongs to another device (manual_in_use)" '{"brand":"Shelly","model":"RGBW2","manuals":["shelly/dimmer_2_guide.pdf"]}'
run "UC-02: manual that does not exist (manual_not_found)" '{"brand":"Shelly","model":"RGBW2","manuals":["shelly/missing.pdf"]}'
run "UC-02: paths outside the folder (manual_not_allowed)" '{"brand":"X","model":"Y","manuals":["../secret.pdf","/etc/passwd","~/a.pdf","%2e%2e/a.pdf"]}'
run "UC-02: required fields missing (invalid_fields)" '{"brand":" ","manuals":[]}'
run "UC-02: no device at all" 'null'
run "UC-01: a valid one that is still free" '{"brand":"Shelly","model":"RGBW2","manuals":["shelly/rgbw2_app_guide.pdf"]}'
run "UC-05: find by a unique property" '{"model":"Double Smart"}' find
run "UC-05: find by several properties (accents, case and spaces are ignored)" '{"brand":"FIBARO","location":" salón ","device_type":"switch"}' find
run "UC-06: find several devices that share a value" '{"brand":"shelly"}' find
run "UC-06: every property must match" '{"brand":"Shelly","model":"RGBW2"}' find
run "UC-06: list every device (no properties)" '{}' find
run "UC-07: nothing matches (message lists the properties)" '{"brand":"Samsung","location":"Garage"}' find
run "UC-07: only part of a value does not match" '{"model":"Dimmer"}' find
echo; echo "### Stored:"; ./db.sh "SELECT device_id, brand, model, location FROM devices; SELECT device_id, manual, COUNT(*) AS chunks FROM chunks GROUP BY device_id, manual;"
