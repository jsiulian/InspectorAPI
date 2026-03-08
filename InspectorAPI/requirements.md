# InspectorAPI — Requirements

## About

InspectorAPI is a cross-platform desktop HTTP client built with .NET 10 and Avalonia UI. It lets developers compose and send HTTP requests, inspect responses, and organise requests into collections — similar in purpose to tools like Postman or Insomnia, but as a lightweight native desktop application.

Key capabilities:
- Compose requests with any HTTP method, URL, headers, query parameters, and body (raw text, form URL-encoded, or multipart form-data)
- Inspect responses: status, timing, size, formatted body, and response headers
- Organise requests into named collections and folders, with save, rename, duplicate, and delete support
- Import and export collections in native JSON format or Postman v2.1 format
- Persistent storage of collections on disk per OS conventions

> Living document. Updated as new requests are made.

---

## 1. Tab Bar

### 1.1 Tab Tooltips
Hovering over a tab must show a tooltip containing:
- The full request name (bold)
- The URL beneath it (smaller, muted), only if a URL is set

### 1.2 Duplicate Tab
A button must allow duplicating the current tab, creating an identical copy with the name prefixed by "Copy of".

---

## 2. Request Headers

### 2.1 Default Headers
New request tabs must be pre-populated with the following headers:
- `Accept: application/json`
- `User-Agent: InspectorAPI`

### 2.2 Header Name Autocomplete
The header name field must provide autocomplete suggestions from a list of common HTTP request headers, filtered by prefix as the user types.

---

## 3. Query Parameters

### 3.1 Bidirectional URL ↔ Query Params Sync
- Editing the URL (including typing query parameters directly) must update the Query Params list.
- Editing, adding, or removing rows in the Query Params list must update the URL.
- Disabled parameters must be preserved in the list but excluded from the URL.
- The sync must not cause infinite update loops.

---

## 4. Request Body

### 4.1 Content Type Switcher
The Body tab must include a Content-Type dropdown. Switching the value changes the body editing UI:

| Content-Type | UI shown |
|---|---|
| `application/x-www-form-urlencoded` | Key-value parameter list (checkbox, name, value, remove) |
| `multipart/form-data` | Key-value-contenttype list (checkbox, name, value, part content-type, remove) |
| All other types | Free-text area |

### 4.2 Switching to a Form Type — Auto-populate
When switching to `application/x-www-form-urlencoded` from the dropdown:
- If the current body text parses as URL-encoded pairs (`key=value&…`), automatically populate the form params list and clear the text body.
- If it does not parse as URL-encoded, show a confirmation dialog warning that the body will be cleared. The user may proceed (clears body) or cancel (reverts the content-type selection).

When switching to `multipart/form-data`:
- Always show the confirmation dialog if the body text is non-empty, since raw multipart cannot be reliably parsed.

### 4.3 Content-Type Header ↔ Body Dropdown Sync (Bidirectional)
- Selecting a content type in the Body tab dropdown must add or update the `Content-Type` header in the Headers tab.
- Editing the `Content-Type` header value in the Headers tab must update the Body tab dropdown selection.
- If the value typed in the header does not exist in the standard dropdown list, it must be added to the dropdown dynamically so it remains selectable. At most one such custom value is kept in the dropdown at any time — partial keystrokes must not accumulate.

### 4.4 Legacy Save Migration
Saved requests that stored form-urlencoded body content as raw text (before the form params UI existed) must be automatically parsed into the form params list when opened.

---

## 5. Collections Panel

### 5.1 Save Persistence
Saving a request that already exists in a collection must update it in place (not create a duplicate).

### 5.2 Rename Consistency
Renaming a request in the collection panel must immediately update the name shown in the tab when that request is reopened.

### 5.3 Method Badge After Save
Changing the HTTP method and saving must update the method badge displayed next to the request name in the collection tree.

---

## 6. Visual / Theme

### 6.1 Accent Colour Buttons
Action buttons (Send, dialog OK/Save) must use the Fluent system accent colour rather than a hardcoded colour.

### 6.2 Active Tab Background
The active tab must have the same background colour as the request bar (`SidebarBg`), visually connecting the two. Inactive tabs must use a distinct, lighter colour. Both states must be correct in light and dark modes.

---

## 7. Dialogs

### 7.1 Auto-Focus
When any dialog opens (new collection, rename, save request, body-clear confirmation), focus must move to the primary text input automatically, with any existing text selected.

---

## 8. Resizable Panels

### 8.1 Minimum Sizes
All resizable panels must have minimum dimensions so they cannot be collapsed to zero:
- Sidebar: min-width 160 px
- Main content area: min-width 460 px
- Request configuration rows (tab area): min-height 80 px
- Response panel: min-height 80 px
- URL bar columns: method selector min-width 100 px, URL field min-width 120 px

---

## 9. Documentation

### 9.1 README
The repository must contain a `README.md` covering:
- Feature overview
- Dependency table (.NET version, Avalonia version, CommunityToolkit.Mvvm version)
- Build instructions from the command line (restore, run, publish) with runtime-identifier examples
- Project structure
- Data storage paths per operating system

---

## 10. Raw Request Editor

### 10.1 Editable Raw View
The Raw tab in the request panel must be a fully editable text area. Editing it must instantly update all other request fields (method, URL, headers, query params, body).

### 10.2 Bidirectional Sync
Changes in any other field (URL bar, Headers tab, Query Params, Body tab, method selector) must instantly update the raw text. Changes made directly in the raw text must propagate back to those fields.

### 10.3 No Circular Updates
Sync in either direction must not cause infinite update loops. A `_syncingRaw` guard ensures that rebuilding the raw text from fields does not re-trigger field parsing, and vice versa.

### 10.4 Parsing Rules
When the raw text is edited:
- The first line is parsed as `METHOD path HTTP/version`. If the method is not a recognised HTTP verb, no changes are applied (protects against partially typed input).
- The `Host:` line is used together with the current URL scheme to reconstruct the full URL.
- All other headers replace the Headers tab contents.
- The body (text after the blank separator line) is applied as raw body text, or parsed into form params if the content type is `application/x-www-form-urlencoded`.
- If the content type is `multipart/form-data` and the body is the placeholder string, the Form Parts list is left untouched.
