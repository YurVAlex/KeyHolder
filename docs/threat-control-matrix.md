# Architecture and threat-control matrix

This document maps each major threat to a plain-language architecture part, its place in the data flow, the chosen response, and the remaining exposure.

## Priority labels

| Label | Meaning |
|---|---|
| **Required** | Needed for the first complete implementation |
| **Before real secrets** | Fake-data prototype may precede it, but real credentials should not |
| **Partial / accepted** | Reasonable mitigation exists, but this app cannot fully solve the threat |
| **Deferred** | Useful but intentionally postponed to avoid disproportionate complexity |
| **Not needed** | Another simpler control covers the requirement or the feature is outside scope |

## Data-flow map

```text
                       E: HTTPS/S3 API
                             │
                             ▼
A: local SQLite  ◄──►  D: desktop app  ◄──►  F: private R2
       │                     │
       │                     ├──► C: memory/UI/clipboard
       │                     │
       └──► B: temporary encrypted snapshot and restore database

G: Windows Credential Manager stores R2 credentials
H: build, dependencies, installer, and source repository
```

## Architecture parts

| ID | Part | Plain-language description | Data-flow job |
|---|---|---|---|
| C01 | Split client-side encryption | Each entry has a metadata envelope (title, username, email, URL, `d-tags`, `s-tags`) and a secret envelope (password, notes). | Plaintext stops at D/C; A, B, and F receive two ciphertext envelopes per entry. |
| C02 | Argon2id and random salt | Turns the master password into a key while making password guessing costly. | Runs in D at unlock; the salt and cost settings may be stored, but the result is not. |
| C03 | Random vault key, wrapping, and HKDF separation | The password-derived key wraps one random vault key. HKDF-SHA-256 derives metadata, secret, and manifest keys using fixed versioned contexts. | Makes password changes small and prevents one effective key from being reused for different data purposes. |
| C04 | AES-256-GCM | Separately hides each metadata and secret payload and detects changed ciphertext. | Encrypts before A/B/F and authenticates an individual envelope before plaintext reaches C. |
| C05 | Fresh random nonce | A different nonce is generated for every encryption under one key. | Prevents dangerous AES-GCM nonce reuse. |
| C06 | Authenticated context | Vault ID, entry ID, envelope kind, and format version are authenticated with each envelope. | Detects record swapping or using metadata ciphertext as secret ciphertext. |
| C07 | Versioned encrypted envelope | Every protected value identifies its supported format. | Enables safe rejection and controlled migrations instead of guessing. |
| C08 | Authenticated complete manifest | The snapshot contains a complete record list protected by HMAC-SHA-256 under a domain-separated key derived from the vault key with HKDF-SHA-256. | Detects removed, added, reordered, or mismatched records in B/F. |
| C09 | Immutable snapshot object | Every publication gets a random object name and existing snapshots are never rewritten. | Preserves complete versions in F and avoids in-place partial updates. |
| C10 | Snapshot lineage | Snapshot ID, parent ID, and generation describe ancestry. | Detects expected successors and divergent writers without trusting clocks. |
| C11 | Untrusted pointer metadata | `latest.json` is only a hint until its values match the authenticated manifest. | Allows a pre-unlock prompt without treating R2 text as proof. |
| C12 | Conditional pointer update | Replace `latest.json` only if its ETag is unchanged; create with `If-None-Match: *`. | Detects another publication between fetch and publish. |
| C13 | Staged restore and atomic swap | Download and validate to temporary files/databases, preserve rollback, then replace. | Prevents a bad or interrupted remote update from destroying A. |
| C14 | Single-writer manual workflow | User fetches before editing and explicitly publishes after editing. | Avoids background races and removes field-level merge logic. |
| C15 | Local dirty and applied-snapshot state | SQLite records the applied snapshot and whether local content changed afterward. | Distinguishes equal, newer-remote, unpublished-local, and divergent states. |
| C16 | Private R2 plus HTTPS | R2 stores objects privately and transport uses standard TLS. | Protects E/F transport and provides remote availability. |
| C17 | Bucket-restricted R2 credential | A scoped access key authorizes only the dedicated bucket; Credential Manager protects the local copy. | Authorizes E/F without sending the master password. |
| C18 | Size, schema, and path validation | Metadata and downloads have strict formats and bounds. | Rejects malformed or resource-exhausting R2 content before restore. |
| C19 | SHA-256 byte check | Pointer records expected snapshot bytes. | Detects transfer errors; cryptographic authenticity still comes from C04/C08. |
| C20 | Offline-first local vault | Existing SQLite opens without R2. | R2 outage cannot block access to A. |
| C21 | Automatic lock and short plaintext lifetime | Plaintext metadata, temporary selected-entry secrets, and keys are dropped after inactivity or Windows lock. | Reduces exposure time at C. |
| C22 | Lazy Reveal/Copy and clipboard timeout | Unlock decrypts metadata only. The fixed `••••••••` placeholder remains until explicit Reveal or Copy decrypts the selected secret envelope; clipboard is cleared later. | Reduces password plaintext lifetime and avoids revealing password length, but cannot eliminate clipboard exposure. |
| C23 | Redacted logging | Logs contain allowlisted status and request IDs, never secrets, bodies, or authorization data. | Reduces leakage from C, E, F, and H. |
| C24 | Independent encrypted backup | A tested copy exists outside R2 and the working disk. | Recovers from deletion, account loss, or bad publication. |
| C25 | Trusted build and dependencies | Reviewed changes, minimal pinned packages, scanning, and controlled builds reduce malicious-code risk. | Protects H; client code can otherwise steal plaintext at C. |
| C26 | Recovery-key wrapping | A separately stored random key provides another wrapping of the vault key. | Allows offline recovery when the master password is forgotten. |

## Threat-to-control matrix

### Stored data and cryptography

| ID | Threat and location | Controls and process | Decision | Remaining exposure |
|---|---|---|---|---|
| T01 | Local SQLite, rollback, or `.pvault` is copied at A/B. | **C01–C07:** only ciphertext and a password-wrapped vault key are present. | **Required** | A weak master password can be guessed offline. |
| T02 | The R2 bucket is copied at F. | **C01–C08, C16:** R2 has encrypted envelopes and an authenticated manifest, not the vault key. | **Required** | Object names, record IDs/count, size, upload time, and pointer metadata remain visible. |
| T03 | User chooses a weak master password. | **C02:** Argon2id raises guess cost; UI recommends a long unique passphrase. | **Partial / accepted** | Software cannot make a predictable password strong. |
| T04 | Ciphertext is changed. | **C04:** AES-GCM verifies before releasing plaintext. | **Required** | An attacker can still delete or hide the object. |
| T05 | A valid envelope is removed or metadata is paired with a different secret envelope in a snapshot. | **C08:** the authenticated complete manifest commits to both exact envelopes for every entry. | **Required** | Snapshot construction and parser need thorough tests. |
| T06 | Envelope is moved to another ID, vault, or envelope kind. | **C06:** identifiers and `metadata`/`secret` kind are authenticated as context. | **Required** | Identifiers and envelope kind remain visible. |
| T07 | AES-GCM nonce is reused. | **C05:** only the crypto component generates a fresh random nonce. | **Required** | Random collision is extremely unlikely but the design still needs review. |
| T08 | Unknown or old format is misread. | **C07, C18:** explicit versions, bounded parser, reject unknown formats. | **Required** | Keep supported migration paths few and tested. |
| T09 | Master password, plaintext vault key, derived key, or secret payload is serialized or logged. | **C03, C22, C23:** serializable models expose only wrapped keys/envelopes; tests scan artifacts for known test secrets. | **Required** | Temporary memory copies remain possible. |
| T10 | Master password is forgotten. | **C26:** recovery key unwraps the same vault key. | **Required if recovery is offered** | Losing both password and recovery key makes recovery impossible by design. |
| T11 | Old snapshot plus old password survives a password change. | **C03:** current header gets a new wrapper, but immutable history remains. | **Partial / accepted** | Retroactive protection requires full vault-key rotation and history removal, deferred as an advanced operation. |

### Desktop process and clipboard

| ID | Threat and location | Controls and process | Decision | Remaining exposure |
|---|---|---|---|---|
| T12 | Plaintext or key remains in .NET memory at C. | **C01, C21, C22:** keep searchable metadata while unlocked, leave all secret envelopes encrypted until selected, lock promptly, and clear temporary secret buffers. | **Partial / accepted** | Metadata remains plaintext while unlocked. Garbage collection and paging prevent guaranteed erasure. The in-memory vault key can derive the secret key. |
| T13 | Keylogger records the master password. | Endpoint hygiene and trusted Windows environment. | **Partial / accepted** | Cryptography cannot protect input before the app receives it. |
| T14 | Malware/debugger reads unlocked process. | **C21, C22, C25:** lazy decryption reduces incidental password plaintext, while lock and trusted builds reduce exposure. | **Partial / accepted** | Malware controlling the process can use the vault key to derive the secret key and decrypt every secret envelope. Reveal is not a separate authorization boundary. |
| T15 | Another person uses an unattended unlocked PC. | **C21:** idle, sleep, and Windows-lock handling. | **Required** | Exposure remains during the configured unlocked interval. |
| T16 | Another process reads clipboard. | **C22:** decrypt only selected secret, copy only its password, clear temporary payload, timeout, ownership check, and clipboard-history warning. | **Partial / accepted** | Clearing cannot undo a copy already made. |
| T17 | Password or its length is captured from screen. | **C22:** fixed-length placeholder, explicit reveal, and short reveal timeout. | **Partial / accepted** | During reveal, screen recorders, remote tools, cameras, and shoulder surfing can capture the password. |

### R2 metadata, credentials, and publication

| ID | Threat and location | Controls and process | Decision | Remaining exposure |
|---|---|---|---|---|
| T18 | Network traffic is observed or modified at E. | **C04, C08, C16:** HTTPS plus end-to-end encrypted/authenticated content. | **Required** | Endpoint and timing metadata remain visible. |
| T19 | R2 credential is stolen. | **C17, C23:** Credential Manager, bucket restriction, no logs, rotation. | **Required** | Attacker may read or destroy encrypted objects until revocation. |
| T20 | Secret entry data is placed in `latest.json` comment. | **C11:** UI labels comments nonsensitive and validates limits; documentation forbids secrets. | **Partial / accepted** | The app cannot reliably classify every user-written secret. Keep comments generic. |
| T21 | Attacker changes pointer comment, timestamp, or target. | **C08, C11, C13:** metadata is untrusted; internal authenticated values must match before replacement. | **Required** | False text may be visible before the user unlocks. |
| T22 | Wrong clock makes an older snapshot appear newer. | **C10:** compare IDs and parent lineage; timestamps are display only. | **Required** | User may still misinterpret displayed time, so status text must explain it. |
| T23 | Two devices publish at once. | **C09, C12, C14:** both immutable snapshots survive; ETag condition lets only one pointer update succeed. | **Required** | User must choose between complete snapshots; no field merge exists. |
| T24 | Pointer condition fails after snapshot upload. | **C09, C12:** pointer remains unchanged and uploaded object is retained as unreferenced. | **Required** | Cleanup must not delete it before conflict recovery/retention expires. |
| T25 | Malformed or huge metadata/snapshot exhausts resources. | **C18:** strict schema, IDs, count, lengths, file size, timeouts, and safe temporary paths. | **Required** | Parser defects remain possible; fuzz and integration tests reduce risk. |
| T26 | R2 presents an old valid snapshot. | **C10, C15:** existing device compares trusted applied ID and expected lineage. | **Partial / accepted** | A brand-new device has no trusted checkpoint. Full rollback prevention needs another trust source. |
| T27 | R2 deletes objects or denies service. | **C20, C24:** local vault works offline and independent backup can restore. | **Partial / accepted** | Remote freshness/publication is unavailable until service returns. |
| T28 | Shared R2 credential cannot identify/revoke one device. | Prefer separate bucket-scoped credentials per device where operationally practical. | **Deferred** | With one shared key, rotation affects every device. Version 1 prioritizes simple setup and single-writer use. |

### Restore, local reliability, and operations

| ID | Threat and location | Controls and process | Decision | Remaining exposure |
|---|---|---|---|---|
| T29 | Live SQLite is copied while changing. | Snapshot through SQLite backup/export, briefly block app writes, then validate. | **Required** | Never upload the live `.db`, `-wal`, or `-shm` files. |
| T30 | Download fails halfway. | **C13:** write a bounded temporary file; current database remains untouched. | **Required** | Temporary files need startup cleanup. |
| T31 | Restore fails or app crashes. | **C13:** restore to new SQLite, run checks, preserve rollback, atomically swap only at end. | **Required** | Filesystem failure may still require manual rollback or independent backup. |
| T32 | Local unpublished changes are overwritten by remote. | **C14, C15:** dirty state plus remote mismatch blocks ordinary restore/publish and requires explicit conflict choice. | **Required** | Choosing one whole snapshot can intentionally discard the other; retain rollback first. |
| T33 | User forgets to publish. | Persistent visible dirty indicator and explicit status. | **Partial / accepted** | Manual workflow necessarily depends on user action. |
| T34 | Pointer update succeeded but client did not receive response. | Read `latest.json` again and compare snapshot ID before reporting failure or retrying. | **Required** | Do not blindly repeat with a new ETag. |
| T35 | R2 is treated as the only backup. | **C24:** independent encrypted export and restore drill. | **Before real secrets** | A backup never restored is unproven. |
| T36 | Malicious/altered application steals secrets. | **C25:** controlled source, dependencies, build, and installer origin. | **Before real secrets** | Signing identifies origin; it does not prove absence of bugs. |
| T37 | R2 key or secret is committed to source/configuration. | **C17, C23:** Credential Manager, example-only configuration, secret scanning. | **Required** | A leaked credential must be revoked even after repository removal. |

## Deliberate simplifications

| Omitted feature/control | Decision and reason |
|---|---|
| Worker, D1, KV, queues, Durable Objects | **Not needed:** direct private R2 object access supports this manual whole-snapshot workflow. |
| Automatic/background synchronization | **Not needed:** explicit check and publish makes state transitions visible and testable. |
| Field-level or record-level merge | **Deferred:** requires base revisions, tombstones, and conflict UI approaching a full sync engine. |
| Timestamp-based “newest wins” | **Rejected:** clocks are not a reliable ordering or conflict mechanism. |
| Uploading live SQLite | **Rejected:** WAL state and concurrent writes can produce an inconsistent replica. |
| Git/GitHub storage | **Not needed:** binary commits, repository credentials, and merge behavior add no benefit over immutable R2 objects. |
| OneDrive/Dropbox folder | **Not selected:** the app cannot deterministically know whether the local sync folder has fetched the newest remote object without another API. |
| SQLCipher | **Not needed initially:** per-entry encryption also protects snapshots and avoids native deployment complexity. |
| Searchable cloud index | **Not needed:** search locally after unlock. |
| Certificate pinning | **Not needed:** standard Windows HTTPS validation is reliable and avoids rotation outages. |
| Anti-debugging and obfuscation | **Not a dependable control:** they do not defeat a machine owner or privileged malware. |
| Record-size padding and traffic shaping | **Deferred:** accept object size and timing leakage for a personal vault. |
| Complete malicious-R2 rollback prevention | **Deferred:** requires another trusted checkpoint or transparency mechanism. |
| Remote wipe | **Not promised:** offline devices cannot be reliably erased; rotate R2 credentials instead. |
| DPAPI vault unlock | **Deferred:** entering the master password each run is simpler and avoids another unlock path. |
| Master-password prompt for every Reveal/Copy | **Not selected initially:** lazy decryption reduces accidental exposure without repeated prompts. Because the vault key remains in memory, Reveal/Copy is not described as an independent lock. |

## Minimum implementation order

```text
1. Offline fake-data vault
        ↓
2. C01–C08: split metadata/secret encryption and authenticated snapshot
        ↓
3. C13, C21–C24: safe restore, lock, clipboard, logs, backup
        ↓
4. Prove local export and restore
        ↓
5. C09–C20: direct R2 check/download/publish workflow
        ↓
6. C25: trusted build and dependency controls
        ↓
7. Consider limited real use only after review
```
