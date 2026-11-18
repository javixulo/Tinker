# Home Manuals MCP (C#) — Functional Specification (DRAFT)

> Draft in review: use cases and acceptance criteria, one section per tool. The five tools (`register_device`, `delete_device`, `find_devices`, `search_manuals` and `update_device`) are drafted. All five are implemented.

## 1. Device information model

| Field | Notes |
|---|---|
| Device type | Washing machine, TV, actuator, remote control… |
| Brand | |
| Color | |
| Model | |
| Location in the home | |
| Manuals | Zero or more files from the user's folder. A device can have none (e.g. it never had one, or they were removed). |
| Invoice | |
| URLs | List of links (e.g. online manuals, support pages). |
| Purchase date | |
| Warranty end date | |
| Status | See "Status" below. |

### Status

| Status | Meaning |
|---|---|
| `active` | In use at home. It is the default when a device is registered without a status |
| `inactive` | At home, but not in use now (e.g. broken or waiting for a repair) |
| `archived` | No longer relevant day to day. Kept in the inventory only as a record (manuals, invoice, warranty) |

The status is informational: any change from one status to another is allowed, and the MCP enforces no workflow. A device always has a status, so it cannot be emptied. The status says nothing about the location: a device of any status may or may not have one.

### Normalization of stored values

Text is normalized when it is written (`register_device` and `update_device`), so the inventory always shows the same format, whatever the client sent. Spaces before and after are removed and the spaces between words are collapsed to one.

| Field | Stored as | Example |
|---|---|---|
| `brand` | Accents are removed and so is any character that is not a letter, a digit, a space, a hyphen or an ampersand (`&`). Then, if it is entirely in uppercase, it is kept as it is (it may be an acronym); otherwise, first letter of each word in uppercase and the rest in lowercase | `LG` → `LG`; `philips hué` → `Philips Hue`; `bang & olufsen` → `Bang & Olufsen`; `B.S.H.` → `BSH` |
| `model` | All uppercase. Characters such as hyphens or slashes are kept, because models are often written with them | `qe55q60-a/b` → `QE55Q60-A/B` |
| `device_type`, `color`, `location` | First character in uppercase, the rest as given | `lavadora` → `Lavadora` |
| `manuals` | Not changed: they must match the name of the file and its path | |
| `invoice`, `urls` | Not changed | |

The comparison used to find devices and to detect duplicates ignores case, accents and spaces (`TextKeys.Fold`), so it does not depend on this format. For the brand, a lookup (`find_devices`, `delete_device`) also removes from the brand given the characters that are removed when it is stored. The tools return the values as stored.

### Data stores

The MCP keeps its data in two stores with different jobs:

- **Inventory** (devices): one record per device with the fields above, including the list of its manuals (paths relative to the manuals folder). It is where `device_id` is assigned.
- **Manuals index**: the content of the manuals, split into chunks that can be searched by meaning. Every chunk carries the `device_id` of the device it belongs to, plus the manual it comes from and its page. That `device_id` is what links the two stores.

The manual files themselves stay in the manuals folder, which belongs to the user and is never modified.

### Types used by the tools

**Device**: a device as the inventory keeps it. Fields of the information model above, plus `device_id`.

| Field | Type | Notes |
|---|---|---|
| `device_id` | integer | Assigned by the inventory when the device is registered. Never provided by the client |
| `brand` | text | Required to register |
| `model` | text | Required to register |
| `manuals` | list of text | Optional. Paths relative to the manuals folder |
| `device_type` | text | Optional |
| `color` | text | Optional |
| `location` | text | Optional. Location in the home |
| `invoice` | text | Optional |
| `urls` | list of text | Optional |
| `purchase_date` | date | Optional (`YYYY-MM-DD`) |
| `warranty_end_date` | date | Optional (`YYYY-MM-DD`) |
| `status` | text | Optional. One of the statuses of "Status" above. `active` when not given at registration |

**Error**: one reason why a tool could not do what the client asked.

| Field | Type | Notes |
|---|---|---|
| `code` | text | Identifies the rule that was broken or the failure. Listed in each tool |
| `message` | text | Written for the AI that consumes the tool: what happened and what to do next |
| `device` | Device | Only when the error refers to one device that is already registered |
| `devices` | list of Device | Only when the error refers to several devices |

**Chunk**: an excerpt of a manual.

| Field | Type | Notes |
|---|---|---|
| `text` | text | The excerpt |
| `device_id` | integer | The device the manual belongs to |
| `manual` | text | Path of the manual, relative to the manuals folder |
| `page` | integer | Page of the manual, starting at 1 |

## 2. Tools

Each tool has its own section with its use cases and acceptance criteria. The caller of every tool is an AI client, which sends structured arguments. Whatever the outcome, the result carries what the client needs to decide what to do next (see the technical spec for the schemas).

### 2.1 Tool `register_device`

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `device` | Device | Yes | `brand` and `model` are required. `device_id` is not provided: the inventory assigns it |

**Output** (`RegistrationResult`)

| Field | Type | Notes |
|---|---|---|
| `ok` | boolean | `true` when the device was registered |
| `errors` | list of Error | Empty when `ok` is `true`. Codes: `invalid_fields`, `manual_not_allowed`, `manual_not_found`, `manual_unreadable`, `device_exists`, `manual_in_use`, `failed`. `device_exists` and `manual_in_use` carry the `device` already registered |
| `device` | Device | The device as stored, with its `device_id`. Empty when it was not registered |

A rejection is not a tool error: it comes as `ok=false` with its `errors`.

**Preconditions**

- The manuals folder is configured.
- Every manual given is a file inside the manuals folder (possibly in a subfolder, e.g. one per brand), given by its path relative to that folder.

#### UC-01 · Register a device

The client registers a new device, optionally together with its manuals. The device is recorded in the inventory with its values normalized (section 1), and the content of its manuals, if any, is read and stored in the manuals index, linked to the device by its `device_id`.

**Required information:** brand and model. All the other fields of the device information model are optional, manuals included: a device without manuals is valid.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | Register with required fields | | The client registers a device with brand and model only | `ok=true`. The inventory has a new record with a new `device_id`, that brand and model, no manuals and the status `active`, and the device is returned as stored. Nothing is added to the manuals index |
| 2 | Register with optional fields | A manual exists in the manuals folder | The client registers a device with the required fields and also others, e.g. color and location | The inventory record has all the fields provided |
| 3 | Register with several manuals | Several manuals exist in the manuals folder | The client registers a device with all of them | The inventory record lists all its manuals |
| 4 | Manual in a subfolder | `shelly/dimmer_2_guide.pdf` exists in the manuals folder | The client registers a device with that relative path | The inventory record has that manual, with its relative path |
| 5 | Manual content is indexed | A manual exists in the manuals folder | The client registers a device with that manual | The text of the manual is stored in the manuals index as chunks, each one with the `device_id` of the new inventory record, the manual it comes from and its page |
| 6 | Content of all manuals is indexed | Several manuals exist in the manuals folder | The client registers a device with all of them | The manuals index has chunks of every manual, all with the `device_id` of the device |
| 7 | Content stays with its own device | A device is registered with its manual | The client registers a second device with a different manual | The new chunks carry only the `device_id` of the second device, and the chunks of the first device are unchanged |
| 8 | Values are normalized | A manual exists in the manuals folder | The client registers brand `philips hué`, model `qe55q60-a/b`, location `salón` and that manual | The record has brand `Philips Hue` (accent removed), model `QE55Q60-A/B` (hyphen and slash kept) and location `Salón`, and the manual exactly as given. The device is returned as stored |
| 9 | Status | | The client registers a device with a valid status, e.g. `archived`, and another without status | The first has the status given and the second `active` |
| 10 | Manual repeated | A manual exists in the manuals folder | The client registers a device whose list has the same manual more than once, or written in different ways that lead to the same file (e.g. `shelly/a.pdf` and `shelly/./a.pdf`) | The record lists the manual once, and its content is indexed once |

#### UC-02 · Reject an invalid registration

The registration is rejected if it breaks any of these rules:

- **Required fields are provided.** Brand and model. A value that is empty or has only spaces counts as not provided, and the brand is judged once normalized (section 1).
- **Every manual is inside the manuals folder.** An absolute path, a path that contains `..`, `~` or encoded sequences, or a link that points outside the folder is not allowed.
- **Every manual exists** in the manuals folder.
- **Every manual can be read.** A manual that is damaged, protected by a password or not a PDF cannot be used (`manual_unreadable`). A manual that opens but has no text, e.g. a scanned one, is accepted.
- **The device is not already registered.** Two devices are the same when they have the same brand and the same model, ignoring upper and lower case, accents and the spaces before, after and between words (`Philips Hué` and `philips  hue` are the same device). It is the same comparison `find_devices` uses.
- **Every manual is new.** A manual can belong to only one device.
- **The status is valid** when it is given: one of the statuses of section 1 (`invalid_fields`).

**Errors.** There is one error code per rule: `invalid_fields`, `manual_not_allowed`, `manual_not_found`, `manual_unreadable`, `device_exists` and `manual_in_use`, plus `failed` for unexpected failures. When a rule can be broken in several ways, the message says which one and what to fix. `device_exists` and `manual_in_use` also return the device already registered. No error reveals the location of the manuals folder nor whether a file outside it exists.

A rejected registration is rejected as a whole, and the client is told every reason at once, so it can fix everything in one go.

In every criterion below, "rejected" means: `ok=false` with the errors listed, nothing from the registration is in the inventory or in the manuals index (a complete rollback), no file outside the manuals folder is read, the devices and manuals registered before are unchanged, and a warning with the reason is logged.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | Device already registered | A device is registered with brand "Samsung" and model "QE55Q60" | The client registers another device with the same brand and model, even with a different manual | Rejected, with a `device_exists` error that returns the device already registered, with all its information |
| 2 | Same device written differently | The same device is registered | The client registers brand "samsung" and model " qe55q60 ", or, for a registered `Philips Hue`, brand "PHILIPS" and model "hue" | Rejected, with a `device_exists` error that returns the device already registered, with all its information |
| 3 | Registering twice | A valid registration has succeeded | The same registration is sent again | The second one is rejected as in criterion 1, and the inventory has only one device |
| 4 | Required fields missing | | The client does not provide all the required fields: brand and model (e.g. an empty brand, a brand with no valid character left once normalized, or a model made only of spaces) | Rejected, with an `invalid_fields` error whose message lists the fields that are missing |
| 5 | Manual not found | `shelly/missing.pdf` does not exist in the manuals folder | The client registers a device with one or more manuals that do not exist | Rejected, with a `manual_not_found` error that lists all of them. It is not a `manual_not_allowed` error: the path is allowed, the file is not there |
| 6 | Manual already belongs to another device | A manual is registered for device A | The client registers a different device with that manual, alone or with others | Rejected, with a `manual_in_use` error that says which manual it is and returns device A, with all its information |
| 7 | Path outside the manuals folder | A file exists outside the manuals folder, and a link inside the folder points to it | The client gives a manual that points outside: an absolute path (e.g. `/etc/passwd`), the path of the link, a home shortcut (`~/Documents/guide.pdf`) or encoded sequences (`%2e%2e/secret.pdf`) | Rejected, with a `manual_not_allowed` error whose message says the manual is outside the manuals folder, and nothing outside the folder is read |
| 8 | Path with `..` | `secret.pdf` exists outside the manuals folder, and `shelly/dimmer_2_guide.pdf` exists inside it | The client gives `../secret.pdf`, `shelly/../../secret.pdf` or `shelly/../shelly/dimmer_2_guide.pdf` (which would end up inside the folder) | Rejected, with a `manual_not_allowed` error whose message says the path cannot contain `..` |
| 9 | Existence outside the folder is not revealed | A file exists outside the manuals folder, and another path outside it does not exist | The client gives each of them | Both get the same `manual_not_allowed` error, with nothing about whether the file exists or about its content |
| 10 | One invalid manual rolls back everything | | The client registers a device with several manuals, one of them not allowed | Rejected, with a `manual_not_allowed` error that says which manual it is. The device is not in the inventory either, and none of the other manuals is in the manuals index |
| 11 | Unexpected failure | A valid registration is made while something outside the request fails, e.g. the database cannot be opened, or the indexing fails after the device record is written | The client registers the device | `ok=false` with a `failed` error whose message says what failed, that it is not caused by the request, and that the client should tell the user instead of retrying. Complete rollback: nothing is left in the inventory or in the manuals index |
| 12 | Manual that cannot be read | Manuals exist in the manuals folder that are damaged, protected by a password or not a PDF | The client registers a device with one or more of them | Rejected, with a `manual_unreadable` error that lists all of them, each with its reason when it is known, and whose message says that those manuals were not saved and why, that the device was not registered either, and that the client should tell the user so they can provide another copy. Nothing is left in the inventory or in the manuals index, not even the manuals that can be read |

### 2.2 Tool `delete_device`

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `device` | Device | Yes | The properties that identify the device: one or more fields of a Device, any of them (including `device_id`). A field that is empty or has only spaces counts as not given, and at least one must be given |

The device is matched as in `find_devices` (so a brand with nothing left once cleaned counts as not given, which can leave no property: `invalid_fields`), and it is deleted only when the properties given single out exactly one registered device. The client does not need to know the `device_id`.

**Output** (`DeletionResult`)

| Field | Type | Notes |
|---|---|---|
| `ok` | boolean | `true` when the device was deleted |
| `device` | Device | The device as it was before being deleted, so the client can tell the user what was deleted. Empty when `ok` is `false` |
| `error` | Error | Empty when `ok` is `true`. Codes: `invalid_fields`, `device_not_found`, `ambiguous_device` (carries the matching `devices`), `failed` |

A device that cannot be deleted is not a tool error: it comes as `ok=false` with its `error`. The tool deletes when it is called, with no confirmation: asking the user is the client's job. It works on one device at a time and never guesses which device is meant. A deletion cannot be undone: the device gets a new `device_id` if it is registered again, and ids are never reused.

**Preconditions**

- The device is registered (`register_device`, UC-01).

#### UC-03 · Delete a device

The client gives enough information to single out a device, and the device is deleted. It is removed from the inventory (its record, its list of manuals and its URLs) and its content is removed from the manuals index (every chunk with its `device_id`), so that the MCP behaves as if the device had never been registered. The manual files in the manuals folder are not touched: they belong to the user.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | Delete by brand and model | Several devices are registered, each with its manuals | The client wants to delete one of them and specifies its brand and model, which are enough to identify it | `ok=true`, and the device is returned as it was. The inventory has no record of it, and the manuals index has no chunk with its `device_id`. The manual files are still in the manuals folder. The other devices keep their inventory records and their chunks unchanged. The deletion is logged |
| 2 | Delete by other properties | Several devices are registered, each with its manuals | The client asks to delete one of them and specifies enough information to identify it, e.g. its type, brand and location | The device is deleted as in criterion 1 |

#### UC-04 · Fail to delete a device

The device is not deleted when the information the client gives does not single out one device, or when something fails while deleting. Either way the client is told what happened and what to do next.

In every criterion below, "fails" means: `ok=false` with the error listed, nothing changes (the inventory and the manuals index are as they were, with every device and its content still in them), and the outcome is logged: a warning when the information is not valid, an error when something fails.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | No properties | Devices are registered | The client gives no property, or only blank ones | Fails, with an `invalid_fields` error whose message says that at least one property is needed to identify the device. Nothing is deleted, even if there is only one device |
| 2 | No device matches | Several devices are registered | The client asks to delete one of them, but no device is found with the properties given | Fails, with a `device_not_found` error whose message says that no registered device matches the properties (listed), that nothing was deleted, and that the client should ask the user for other details or check the registered devices with `find_devices` |
| 3 | Several devices match | Several devices share some properties | The client tries to delete one of them, but the properties given match more than one device (ambiguity) | Fails, with an `ambiguous_device` error that returns the matching devices with all their information, and whose message says that nothing was deleted and that the client should ask the user which one it is, or give more properties |
| 4 | Failure while deleting | Several devices are registered | The client asks to delete one of them and an unexpected failure occurs while deleting | Fails, with a `failed` error whose message says that the deletion failed, that it is not caused by the request, that nothing was deleted, and that the client should tell the user instead of retrying. Complete rollback: the device is still in the inventory and its chunks are still in the manuals index |

### 2.3 Tool `find_devices`

A tool to find devices based on search criteria.

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `device` | Device | Yes | The properties to look for: any fields of a Device, none of them required (including `device_id`). A field that is empty or has only spaces counts as not given |

A device matches when every given field is equal to the same field of the device, comparing the whole value (for a list, when the device has every item given). Text is compared in lowercase, without accents and ignoring the spaces before, after and between words (`Habitación` and ` habitacion ` are the same); part of a value is not a match. The brand given is first cleaned as the brand is when it is stored (section 1: what is not a letter, a digit, a space, a hyphen or `&` is removed), so `B.S.H.` finds a device stored as `BSH`. If nothing is left of it, the brand counts as not given. With no field given, every device matches.

**Output** (`FindDevicesResult`)

| Field | Type | Notes |
|---|---|---|
| `ok` | boolean | `true` when the lookup was done, even if nothing matched |
| `devices` | list of Device | The devices that match, ordered by `device_id`. Empty when none does |
| `message` | text | Set when `devices` is empty: why, and what to do next |
| `error` | Error | Empty when `ok` is `true`. Code: `failed` |

**Preconditions**

- None. With no devices registered the tool answers as in UC-07, criterion 3.

#### UC-05 · Find a single device

The client looks for a device by what it knows about it, and exactly one device matches. It gets that device with all its information, including its `device_id`.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | Find by a unique property | Several devices are registered, and one of them has a property with a value that no other device has | The client looks for devices by that property and value | `ok=true`, and the result has that device and no other, with all its information including its `device_id`. `message` is empty |
| 2 | Find by several properties | Several devices are registered, and some of them share some of their properties | The client looks for devices by a combination of properties that only one of them matches | The result has only that device |
| 3 | Brand written with other characters | A device is registered whose brand was stored without accents or special characters (e.g. `BSH`, `Philips Hue`) | The client looks for it with the brand as the user writes it (e.g. `B.S.H.`, `Philips Hué`) | The result has that device |

#### UC-06 · Find several devices

The client looks for devices by what it knows about them, or lists all of them, and more than one matches. It gets all of them, each with all its information and ordered by `device_id`, so it can decide which one it needs or ask the user.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | Find by a shared property | Several devices are registered, and some of them share the value of a property | The client looks for devices by that property and value | `ok=true`, and the result has every device with that value and no other, each with all its information including its `device_id`, ordered by `device_id`. `message` is empty |
| 2 | Every property must match | Several devices are registered, some of them sharing the values of several properties and others sharing only some | The client looks for devices by several properties | The result has only the devices that match all of them |
| 3 | List every device | Several devices are registered | The client gives no property, or only blank ones | The result has every registered device, ordered by `device_id` |

#### UC-07 · Find devices without a result

The client gets an empty result, or the lookup fails. Either way it is told what happened and what to do next.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | No device matches | Several devices are registered | The client looks for devices, but none matches the properties given | `ok=true`, an empty list, and a `message` that says that no registered device matches the properties (listed), and that the client should ask the user for other details or register the device |
| 2 | The whole value must match | A device is registered | The client looks for devices by a value that is only part of the value of a property of the device | The same as in criterion 1: no device matches |
| 3 | No device is registered | The inventory is empty | The client looks for devices, with or without properties | `ok=true`, an empty list, and a `message` that says that there are no registered devices and that the client should register one |
| 4 | Unexpected failure | Several devices are registered | The client looks for devices and an unexpected failure occurs | `ok=false` with a `failed` error whose message says what failed, that it is not caused by the request, and that the client should tell the user instead of retrying. The failure is logged |

### 2.4 Tool `search_manuals`

A tool to search the manuals of a device. Excerpts with the same relevance come in the order of the manual.

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `device_id` | integer | Yes | The device whose manuals are searched. The client gets it from `find_devices`. If it is missing, the answer is `invalid_fields` |
| `query` | text | Yes | Free text in natural language: what to look for in the manuals, e.g. a question or a description. It has no special syntax (no operators or wildcards). The search is by meaning, not only by exact words. It must not be blank. Spaces before and after are ignored |
| `n_results` | integer | No | Maximum number of excerpts to return. At least 1. Default 5 |

**Output** (`SearchResult`)

| Field | Type | Notes |
|---|---|---|
| `ok` | boolean | `true` when the search was done, even if there is nothing to return |
| `chunks` | list of Chunk | The excerpts, the most relevant first. Empty when there is nothing to return |
| `message` | text | Set when `chunks` is empty: why, and what to do next |
| `error` | Error | Empty when `ok` is `true`. Codes: `invalid_fields`, `device_not_found`, `failed` |

**Preconditions**

- The device is registered, and its manuals are in the manuals index (`register_device`, UC-01).

#### UC-08 · Search the manuals of a device

The client searches the manuals of one device with a query, and gets the excerpts that best answer it, with where each one comes from.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | Search the manuals | A device is registered, and its manuals are indexed | The client searches its manuals with a query | `ok=true`, and the result has the excerpts of its manuals that best match the query, the most relevant first. Each excerpt has its text, the `device_id`, the manual it comes from (relative path) and its page. `message` is empty |
| 2 | Only that device | Several devices are registered, each with its manuals indexed | The client searches the manuals of one of them | Every excerpt belongs to that device, even if the content of another device matches the query better |
| 3 | Number of results | A device has more excerpts than the client asks for | The client searches with a given number of results, or without one | The result has at most that number of excerpts, or at most the default number if the client gives none |

#### UC-09 · Search the manuals without a result

The client gets an empty result, or the search fails. Either way it is told what happened and what to do next.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | The device does not exist | No device is registered with the `device_id` given | The client searches its manuals | `ok=false` with a `device_not_found` error whose message says that the device does not exist and that the client should use `find_devices` to get a valid `device_id` |
| 2 | Invalid arguments | A device is registered | The client searches with a blank query, or with a number of results below one | `ok=false` with an `invalid_fields` error whose message says which argument is invalid and what it must be |
| 3 | The manuals have no text | A device is registered, but no text could be extracted from its manuals | The client searches its manuals | `ok=true`, an empty list, and a `message` that says that its manuals have no extractable text, so nothing can be answered from them, and that the client should tell the user |
| 4 | The device has no manuals | A device is registered without manuals | The client searches its manuals | `ok=true`, an empty list, and a `message` that says that the device has no manuals, so nothing can be answered, and that the client should tell the user, who can add manuals with `update_device` |
| 5 | Unexpected failure | A device is registered | The client searches its manuals and an unexpected failure occurs | `ok=false` with a `failed` error whose message says what failed, that it is not caused by the request, and that the client should tell the user instead of retrying. The failure is logged |

### 2.5 Tool `update_device`

A tool to edit a device that is already registered: its data, its manuals or both. The device keeps its `device_id`.

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `device_id` | integer | Yes | The device to edit. The client gets it from `find_devices`. If it is missing, the answer is `invalid_fields` |
| `changes` | Device | Yes | The fields to change, with their new values. At least one must be given. A field that is not sent keeps its current value. A field sent with an empty value (empty text, only spaces or an empty list) is emptied, but only if it is optional: `brand` and `model` cannot be emptied, nor can `status`. `device_id` is not part of the changes and cannot be changed |

A field given **replaces** the current value as a whole. For a list (`manuals`, `urls`), the list given is the new complete list: to add an item the client sends the current items plus the new one, to remove one it sends the list without it, and to remove all of them it sends an empty list. The client has the current lists from `find_devices`. The new values are normalized as in `register_device` (section 1).

**Output** (`UpdateResult`)

| Field | Type | Notes |
|---|---|---|
| `ok` | boolean | `true` when the device was edited, or when the changes were already the current values |
| `errors` | list of Error | Empty when `ok` is `true`. Codes: `invalid_fields`, `device_not_found`, `manual_not_allowed`, `manual_not_found`, `manual_unreadable`, `device_exists`, `manual_in_use`, `failed`. `device_exists` and `manual_in_use` carry the other `device` that causes the collision |
| `device` | Device | The device as stored after the edit, with its `device_id`. Empty when it was not edited |

A rejection is not a tool error: it comes as `ok=false` with its `errors`. The tool edits when it is called, with no confirmation: asking the user is the client's job. It works on one device at a time.

**Preconditions**

- The manuals folder is configured.
- The device is registered (`register_device`, UC-01).
- Every manual in `changes` is a file inside the manuals folder, given by its path relative to that folder (the same rule as in `register_device`).

#### UC-10 · Update the data of a device

The client changes one or more fields of a registered device, other than its manuals, or empties optional ones. The inventory record is updated, and the fields not sent keep their value. The `device_id` does not change, so the content of the manuals in the manuals index stays linked to the device and is not touched.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | Change one field | Several devices are registered | The client changes one optional field of one of them, e.g. its location | `ok=true`, and the device is returned as stored. The inventory record has the new value, and all its other fields and its `device_id` are as they were. The change is logged |
| 2 | Change several fields | A device is registered | The client changes several fields at once, e.g. status, warranty end date and urls | The inventory record has all the new values. A list given (`urls`) replaces the previous list as a whole |
| 3 | Fields not sent are kept | A device is registered with optional fields filled in | The client changes one field and does not send the others | The fields not sent keep their value |
| 4 | Change brand or model | Two devices are registered, with different brand and model | The client changes the model of one of them to a model that no other device has with that brand | The record has the new model. `find_devices` finds the device by the new model and no longer by the old one. The device and its chunks keep the same `device_id` |
| 5 | Values are normalized | A device is registered | The client changes its brand to `philips`, its model to ` hue-2/b ` and its location to `trastero` | The record has brand `Philips`, model `HUE-2/B` (hyphen and slash kept) and location `Trastero`. The device is returned as stored |
| 6 | A device does not collide with itself | A device is registered | The client writes its brand or model differently, so that it is the same as the current one under the comparison of `find_devices` (case, accents and spaces) | `ok=true`. The record has the new value, normalized as in criterion 5 |
| 7 | Empty an optional field | A device is registered with its optional fields filled in | The client sends any optional field (every field except brand, model and status) with an empty value | `ok=true`. The record has that field empty, and all its other fields are as they were. For a list, an empty list leaves the device without items |
| 8 | The changes are the current values | A device is registered | The client sends values that are already the current ones, once normalized | `ok=true`, and the device is returned unchanged. Nothing is written to the inventory nor to the manuals index |

#### UC-11 · Change the manuals of a device

The client gives a new list of manuals for a registered device. The inventory record gets the new list, and the manuals index is brought in line with it: the content of the manuals that are new is read and indexed with the `device_id` of the device, the chunks of the manuals that are no longer in the list are removed, and the chunks of the manuals that stay are left as they are. The manual files in the manuals folder are not touched: they belong to the user.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | Add a manual | A device is registered with one manual, and another manual exists in the manuals folder | The client sends the list with the current manual and the new one | `ok=true`. The inventory record lists both manuals. The manuals index has the chunks of the new manual, each one with the `device_id` of the device, the manual it comes from and its page. The chunks of the manual that was already there are unchanged |
| 2 | Remove a manual | A device is registered with several manuals | The client sends the list without one of them | The record no longer lists it, and the manuals index has no chunk of it. The chunks of the other manuals are unchanged, and the file is still in the manuals folder |
| 3 | Replace a manual | A device is registered with a manual, and another manual exists in the manuals folder | The client sends a list with only the other manual | The record lists only the other manual. The chunks of the old one are gone and the ones of the new one are in the manuals index |
| 4 | Manual that stays | A device is registered with a manual | The client sends a list that has that manual again | `ok=true`. The manual stays in the record, because it belongs to this very device, and its chunks stay in the manuals index as they were |
| 5 | Remove every manual | A device is registered with several manuals | The client sends an empty list | `ok=true`. The record has no manuals, the manuals index has no chunk of the device, and the rest of its fields are as they were. The files are still in the manuals folder |
| 6 | Add manuals to a device without any | A device is registered without manuals, and a manual exists in the manuals folder | The client sends a list with that manual | The record lists it, and its chunks are in the manuals index with the `device_id` of the device |
| 7 | Manual in a subfolder | `shelly/dimmer_2_guide.pdf` exists in the manuals folder | The client adds that relative path | The record has that manual with its relative path, and it is indexed |
| 8 | Data and manuals together | A device is registered | The client changes its color and its manuals in the same call | Both changes are applied together: the record has the new color and the new list, and the manuals index is in line with it |
| 9 | Manual freed by another device | A manual belongs to device A | The client first removes it from device A and then adds it to device B | Both calls succeed. The chunks of the manual now carry the `device_id` of device B only |
| 10 | Manual whose file is gone | A device is registered with a manual, and the file was later removed from the manuals folder | The client changes another field of the device, or sends a list without that manual | `ok=true`. The change is applied. If the manual stays in the list, it stays in the record with its chunks; if it is removed, it leaves the record and the manuals index |
| 11 | Manual repeated | A device is registered | The client sends a list that has the same manual more than once, or written in different ways that lead to the same file | The record lists the manual once, and its content is indexed once (or stays as it was, if the device already had it) |

#### UC-12 · Reject an invalid update

The update is rejected, as a whole, when any of these rules is broken. The client is told every reason at once, except `device_not_found`, which comes alone.

- The device exists (`device_not_found`).
- There is something to change, and it is valid (`invalid_fields`).
- Every manual new for the device is allowed and exists (`manual_not_allowed`, `manual_not_found`), with the same rules as in `register_device`: nothing outside the manuals folder is read or revealed. The manuals the device already has are not checked again.
- Every manual new for the device can be read (`manual_unreadable`), as in `register_device`.
- The device does not become another registered device (`device_exists`).
- Every manual that is new for the device is not used by another one (`manual_in_use`).

`device_exists` and `manual_in_use` return the other device involved. Unexpected failures are `failed`.

In every criterion below, "rejected" means: `ok=false` with the errors listed, a complete rollback (the inventory and the manuals index are as they were), and the outcome logged.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | The device does not exist | No device is registered with the `device_id` given (it never existed, or it was deleted) | The client edits it | Rejected, with a `device_not_found` error whose message says that the device does not exist, that nothing was changed, and that the client should use `find_devices` to get a valid `device_id`. It is not created: `update_device` never registers a device |
| 2 | Nothing to change | A device is registered | The client gives only the `device_id`, with no field to change | Rejected, with an `invalid_fields` error whose message says that at least one field to change is needed |
| 3 | Invalid values | A device is registered | The client gives a status that is not a valid one, a `device_id` inside `changes`, no `device_id`, or an empty value for a required field (brand, model; a brand that is empty once normalized counts too) or for the status | Rejected, with an `invalid_fields` error whose message says which field is invalid and what it must be (e.g. that the `device_id` cannot be changed, or that the brand cannot be emptied) |
| 4 | Collision on brand and model | Devices A and B are registered, with a different brand and model | The client changes the brand, the model or both of B so that, once the edit is applied, they are the same as those of A under the comparison of `find_devices` | Rejected, with a `device_exists` error that returns device A with all its information, and whose message says that nothing was changed and that the client should check whether the user means device A or choose other values |
| 5 | Manual already belongs to another device | A manual is registered for device A, and device B is registered | The client adds that manual to B, alone or with others | Rejected, with a `manual_in_use` error that says which manual it is and returns device A, with all its information. Device A keeps its manual and its chunks |
| 6 | Manual not found | `shelly/missing.pdf` does not exist in the manuals folder | The client adds one or more manuals that do not exist | Rejected, with a `manual_not_found` error that lists all of them. It is not a `manual_not_allowed` error: the path is allowed, the file is not there |
| 7 | Path outside the manuals folder | A file exists outside the manuals folder, and a link inside the folder points to it | The client adds a manual that points outside: an absolute path, the path of the link, `~/Documents/guide.pdf`, encoded sequences (`%2e%2e/secret.pdf`) or a path with `..` | Rejected, with a `manual_not_allowed` error as in `register_device` (UC-02, criteria 7 to 9): the message says the manual is outside the manuals folder, or that the path cannot contain `..`, and nothing outside the folder is read nor revealed |
| 8 | Several reasons at once | Devices A and B are registered | The client edits B with the brand and model of A, a manual that belongs to A, and a manual that does not exist | Rejected, with a `device_exists`, a `manual_in_use` and a `manual_not_found` error, all in the same answer |
| 9 | One invalid manual rolls back everything | A device is registered with its manuals indexed | The client changes its color, adds a valid manual and a manual that is not allowed | Rejected, with a `manual_not_allowed` error that says which manual it is. The color is not changed, the valid manual is not in the record nor in the manuals index, and the chunks of the current manuals are unchanged |
| 10 | Unexpected failure | A valid update is made while something outside the request fails, e.g. the database cannot be opened, or the indexing of a new manual fails after the record is written | The client edits the device | `ok=false` with a `failed` error whose message says what failed, that it is not caused by the request, that the device was not changed, and that the client should tell the user instead of retrying. Complete rollback: the record is as it was, the chunks of the removed manuals are still in the manuals index, and the chunks of the new ones are not |
| 11 | Manual that cannot be read | A device is registered, and a manual exists in the manuals folder that is damaged, protected by a password or not a PDF | The client adds that manual, alone or with others, and may change other fields too | Rejected, with a `manual_unreadable` error that lists it with its reason when known, and whose message says that it was not saved and why, that nothing was changed, and that the client should tell the user so they can provide another copy. The other manuals of the call and the other changes are not applied either |

## 3. Open questions

- **Status workflow.** **Decided for now:** three statuses (`active`, `inactive`, `archived`) and no workflow: any change from one to another is allowed. Revisit if real use needs more statuses (e.g. sold or discarded) or rules (e.g. an `archived` device that is dropped from `find_devices` by default).
- **Normalization of the brand and of `invoice`.** **Decided:** the brand loses its accents and any character that is not a letter, a digit, a space, a hyphen or an ampersand; then, if it comes entirely in uppercase it is kept as it is, so acronyms such as `LG` or `TCL` survive, and any other brand gets the first letter of each word in uppercase. `invoice` is stored as given, because it may be a path or a reference where the case matters.

- **Case and accents.** **Decided, for every tool:** text is compared in lowercase, without accents and ignoring the spaces before, after and between words. It applies to `find_devices`, to `delete_device` (which matches as it does) and to the duplicate rule of `register_device` (brand and model). One function in the code, `TextKeys.Fold`.
- **Language of the manuals and the queries.** **Decided for now: English only.** All manuals are assumed to be in English, and so are the queries (the client translates them if needed). That allowed an English embedding model (technical spec, section 7). If manuals in other languages appear, the model must change and the index be rebuilt; the steps are in the technical spec.
- **The `query` of `search_manuals`.** **Decided for now:** free natural-language text in English, matched by meaning, as drafted in the input table. The vector store and the embedding model are chosen (technical spec, section 7). The search always returns the best `n_results` excerpts of the device, with no minimum relevance: a weak match is still returned, and the client judges from the text. Keywords, filters or a mix with exact-word search (FTS5) are not needed yet; revisit them if real manuals show that exact terms (model numbers, error codes) are missed.
- **Regenerating the chunks.** Open. How the manuals are split into chunks (target size, maximum size, overlap, how lists are kept together) is a tuning decision that real manuals will keep changing, and a new value only applies to devices registered afterwards: the existing chunks are stale, and today the only way to refresh them is to delete and register the devices again. A tool to rebuild the chunks of one device or of all of them, with the new values as arguments (the manual files stay as they are, and they are read again), would let the values be tuned without losing the inventory. To decide: whether it is an MCP tool (the client could call it by mistake, and it can take minutes) or an operator command outside the MCP; whether the values are arguments or configuration; and what a search returns while the rebuild runs. It shares the "rebuild the index" step of the technical spec (section 7), which also needs it when the embedding model changes.
