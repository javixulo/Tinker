# Home Manuals MCP (C#) — Functional Specification (DRAFT)

> Draft in review: use cases and acceptance criteria, one section per tool. The four tools (`register_device`, `delete_device`, `find_devices` and `search_manuals`) are drafted.

## 1. Device information model

| Field | Notes |
|---|---|
| Device type | Washing machine, TV, actuator, remote control… |
| Brand | |
| Color | |
| Model | |
| Location in the home | |
| Manuals | One or more files from the user's folder. |
| Invoice | |
| URLs | List of links (e.g. online manuals, support pages). |
| Purchase date | |
| Warranty end date | |
| Status | Active, stored, sold, discarded. |

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
| `manuals` | list of text | Required to register. Paths relative to the manuals folder |
| `device_type` | text | Optional |
| `color` | text | Optional |
| `location` | text | Optional. Location in the home |
| `invoice` | text | Optional |
| `urls` | list of text | Optional |
| `purchase_date` | date | Optional (`YYYY-MM-DD`) |
| `warranty_end_date` | date | Optional (`YYYY-MM-DD`) |
| `status` | text | Optional. One of `active`, `stored`, `sold`, `discarded` |

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
| `device` | Device | Yes | `brand`, `model` and `manuals` are required. `device_id` is not provided: the inventory assigns it |

**Output** (`RegistrationResult`)

| Field | Type | Notes |
|---|---|---|
| `ok` | boolean | `true` when the device was registered |
| `errors` | list of Error | Empty when `ok` is `true`. Codes: `invalid_fields`, `manual_not_allowed`, `manual_not_found`, `device_exists`, `manual_in_use`, `failed`. `device_exists` and `manual_in_use` carry the `device` already registered |
| `device` | Device | The device as stored, with its `device_id`. Empty when it was not registered |

A rejection is not a tool error: it comes as `ok=false` with its `errors`.

**Preconditions**

- The manuals folder is configured.
- Every manual is a file inside the manuals folder (possibly in a subfolder, e.g. one per brand), given by its path relative to that folder.

#### UC-01 · Register a device

The client registers a new device together with its manuals. The device is recorded in the inventory, and the content of its manuals is read and stored in the manuals index, linked to the device by its `device_id`.

**Required information:** brand, model and at least one manual. All the other fields of the device information model are optional.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | Register with required fields | A manual exists in the manuals folder | The client registers a device with brand, model and that manual | `ok=true`. The inventory has a new record with a new `device_id`, that brand, model and manual, and the device is returned as stored |
| 2 | Register with optional fields | A manual exists in the manuals folder | The client registers a device with the required fields and also others, e.g. color and location | The inventory record has all the fields provided |
| 3 | Register with several manuals | Several manuals exist in the manuals folder | The client registers a device with all of them | The inventory record lists all its manuals |
| 4 | Manual in a subfolder | `shelly/dimmer_2_guide.pdf` exists in the manuals folder | The client registers a device with that relative path | The inventory record has that manual, with its relative path |
| 5 | Manual content is indexed | A manual exists in the manuals folder | The client registers a device with that manual | The text of the manual is stored in the manuals index as chunks, each one with the `device_id` of the new inventory record, the manual it comes from and its page |
| 6 | Content of all manuals is indexed | Several manuals exist in the manuals folder | The client registers a device with all of them | The manuals index has chunks of every manual, all with the `device_id` of the device |
| 7 | Content stays with its own device | A device is registered with its manual | The client registers a second device with a different manual | The new chunks carry only the `device_id` of the second device, and the chunks of the first device are unchanged |

#### UC-02 · Reject an invalid registration

The registration is rejected if it breaks any of these rules:

- **Required fields are provided.** Brand, model and at least one manual. A value that is empty or has only spaces counts as not provided.
- **Every manual is inside the manuals folder.** An absolute path, a path that contains `..`, `~` or encoded sequences, or a link that points outside the folder is not allowed.
- **Every manual exists** in the manuals folder.
- **The device is not already registered.** Two devices are the same when they have the same brand and the same model, ignoring upper and lower case and the spaces before, after and between words.
- **Every manual is new.** A manual can belong to only one device.
- **The status is valid** when it is given: one of `active`, `stored`, `sold`, `discarded` (`invalid_fields`).

**Errors.** There is one error code per rule: `invalid_fields`, `manual_not_allowed`, `manual_not_found`, `device_exists` and `manual_in_use`, plus `failed` for unexpected failures. When a rule can be broken in several ways, the message says which one and what to fix. `device_exists` and `manual_in_use` also return the device already registered. No error reveals the location of the manuals folder nor whether a file outside it exists.

A rejected registration is rejected as a whole, and the client is told every reason at once, so it can fix everything in one go.

In every criterion below, "rejected" means: `ok=false` with the errors listed, nothing from the registration is in the inventory or in the manuals index (a complete rollback), no file outside the manuals folder is read, the devices and manuals registered before are unchanged, and a warning with the reason is logged.

**Acceptance criteria**

| # | Name | Given | When | Then |
|---|---|---|---|---|
| 1 | Device already registered | A device is registered with brand "Samsung" and model "QE55Q60" | The client registers another device with the same brand and model, even with a different manual | Rejected, with a `device_exists` error that returns the device already registered, with all its information |
| 2 | Same device written differently | The same device is registered | The client registers brand "samsung" and model " qe55q60 " | Rejected, with a `device_exists` error that returns the device already registered, with all its information |
| 3 | Registering twice | A valid registration has succeeded | The same registration is sent again | The second one is rejected as in criterion 1, and the inventory has only one device |
| 4 | Required fields missing | | The client does not provide all the required fields: brand, model and at least one manual (e.g. an empty brand, a model made only of spaces, or no manual) | Rejected, with an `invalid_fields` error whose message lists the fields that are missing |
| 5 | Manual not found | `shelly/missing.pdf` does not exist in the manuals folder | The client registers a device with one or more manuals that do not exist | Rejected, with a `manual_not_found` error that lists all of them. It is not a `manual_not_allowed` error: the path is allowed, the file is not there |
| 6 | Manual already belongs to another device | A manual is registered for device A | The client registers a different device with that manual, alone or with others | Rejected, with a `manual_in_use` error that says which manual it is and returns device A, with all its information |
| 7 | Path outside the manuals folder | A file exists outside the manuals folder, and a link inside the folder points to it | The client gives a manual that points outside: an absolute path (e.g. `/etc/passwd`), the path of the link, a home shortcut (`~/Documents/guide.pdf`) or encoded sequences (`%2e%2e/secret.pdf`) | Rejected, with a `manual_not_allowed` error whose message says the manual is outside the manuals folder, and nothing outside the folder is read |
| 8 | Path with `..` | `secret.pdf` exists outside the manuals folder, and `shelly/dimmer_2_guide.pdf` exists inside it | The client gives `../secret.pdf`, `shelly/../../secret.pdf` or `shelly/../shelly/dimmer_2_guide.pdf` (which would end up inside the folder) | Rejected, with a `manual_not_allowed` error whose message says the path cannot contain `..` |
| 9 | Existence outside the folder is not revealed | A file exists outside the manuals folder, and another path outside it does not exist | The client gives each of them | Both get the same `manual_not_allowed` error, with nothing about whether the file exists or about its content |
| 10 | One invalid manual rolls back everything | | The client registers a device with several manuals, one of them not allowed | Rejected, with a `manual_not_allowed` error that says which manual it is. The device is not in the inventory either, and none of the other manuals is in the manuals index |
| 11 | Unexpected failure | A valid registration is made while something outside the request fails, e.g. the database cannot be opened, or the indexing fails after the device record is written | The client registers the device | `ok=false` with a `failed` error whose message says what failed, that it is not caused by the request, and that the client should tell the user instead of retrying. Complete rollback: nothing is left in the inventory or in the manuals index |

### 2.2 Tool `delete_device`

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `device` | Device | Yes | The properties that identify the device: one or more fields of a Device, any of them (including `device_id`). A field that is empty or has only spaces counts as not given, and at least one must be given |

The device is matched as in `find_devices`, and it is deleted only when the properties given single out exactly one registered device. The client does not need to know the `device_id`.

**Output** (`DeletionResult`)

| Field | Type | Notes |
|---|---|---|
| `ok` | boolean | `true` when the device was deleted |
| `device` | Device | The device as it was before being deleted, so the client can tell the user what was deleted. Empty when `ok` is `false` |
| `error` | Error | Empty when `ok` is `true`. Codes: `invalid_fields`, `device_not_found`, `ambiguous_device` (carries the matching `devices`), `failed` |

A device that cannot be deleted is not a tool error: it comes as `ok=false` with its `error`. The tool deletes when it is called, with no confirmation: asking the user is the client's job. It works on one device at a time and never guesses which device is meant.

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

A device matches when every given field is equal to the same field of the device, comparing the whole value (for a list, when the device has every item given). Text is compared in lowercase, without accents and ignoring the spaces before, after and between words (`Habitación` and ` habitacion ` are the same); part of a value is not a match. With no field given, every device matches.

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

A tool to search the manuals of a device.

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `device_id` | integer | Yes | The device whose manuals are searched. The client gets it from `find_devices` |
| `query` | text | Yes | Free text in natural language: what to look for in the manuals, e.g. a question or a description. It has no special syntax (no operators or wildcards). The search is by meaning, not only by exact words. It must not be blank |
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
| 4 | Unexpected failure | A device is registered | The client searches its manuals and an unexpected failure occurs | `ok=false` with a `failed` error whose message says what failed, that it is not caused by the request, and that the client should tell the user instead of retrying. The failure is logged |

## 3. Open questions

- **Case and accents.** **Decided for `find_devices`** (and so for `delete_device`, which matches as it does): text is compared in lowercase and without accents. **Still to decide:** the duplicate rule of `register_device` (brand and model) ignores case and spaces but not accents, so `Philips Hué` and `Philips Hue` are two devices while a lookup treats them as the same. To align it, the stored keys would have to be rebuilt (technical spec, section 7).
- **Language of the manuals and the queries.** **Decided for now: English only.** All manuals are assumed to be in English, and so are the queries (the client translates them if needed). That allowed an English embedding model (technical spec, section 7). If manuals in other languages appear, the model must change and the index be rebuilt; the steps are in the technical spec.
- **The `query` of `search_manuals`.** To review what the query is and how it is used: free natural-language text matched by meaning (as drafted in the input table), or something else (e.g. keywords, filters, a mix of both). Not clear yet; to revisit when the search is implemented and the vector store and embedding model are chosen.
