# Security model

## Safety statement

Building a password manager is an excellent architecture exercise but an unusually risky first security project. Small mistakes in cryptography, snapshot restore, release delivery, clipboard handling, or logging can expose every stored account.

Treat early releases as prototypes containing fake data. Use a mature password manager for real credentials until this project has:

- automated cryptographic and snapshot-protocol tests;
- a proven independent encrypted backup and restore path;
- controlled or signed releases;
- dependency and secret scanning;
- an independent security review.

For detailed control priorities and accepted limitations, see the [threat-control matrix](threat-control-matrix.md).

## Security goals

1. A copied SQLite file, `.pvault` file, or R2 bucket does not reveal vault fields.
2. Modified or corrupted entries and snapshot manifests are rejected before local replacement.
3. The user explicitly approves every remote restore and publication.
4. Concurrent publication never silently overwrites another device's pointer.
5. R2 failure never prevents the existing local vault from opening offline.
6. A failed restore leaves the current local database intact.
7. Sensitive values do not appear in logs, crash reports, comments, source control, or `latest.json`.
8. Unlocking decrypts searchable metadata but does not decrypt every password or notes payload.

## Explicit non-goals

The design cannot fully protect against:

- malware, a debugger, keylogger, screen recorder, or administrator on an unlocked PC;
- someone who knows the master password or recovery key;
- a malicious desktop build;
- denial of service or deletion by someone controlling R2;
- a consistent old snapshot presented to a brand-new device with no trusted local history;
- metadata analysis of object size, upload time, device name, and non-secret comment;
- forgotten master password when no recovery key exists.

"Client-side encrypted" means R2 does not receive the content key or plaintext. It does not mean R2 learns no metadata or cannot destroy data.

## Assets and credentials

| Asset | Confidential? | Storage rule |
|---|---:|---|
| Master password | Yes | Memory only during unlock |
| Password-derived key | Yes | Brief memory only |
| Vault key | Yes | Memory while unlocked; wrapped form at rest |
| Metadata key | Yes | Derived with HKDF context `metadata-v1`; memory while unlocked |
| Secret key | Yes | Derived with HKDF context `secrets-v1`; only during selected secret operations where practical |
| Recovery key | Yes | User-controlled offline copy; never uploaded in plaintext |
| Metadata fields | Yes at rest | AES-GCM `metadata_envelope`; plaintext in memory after unlock for search and sort |
| Password and notes | Yes | AES-GCM `secret_envelope`; decrypted temporarily only for the selected entry |
| R2 secret access key | Yes | Windows Credential Manager only |
| Snapshot ID and parent ID | No | Local remote state, authenticated manifest, and `latest.json` |
| Timestamp, device name, comment | Treated as public metadata | Must contain no account or secret information |

## Main threat table

| Threat | Primary control | Remaining risk |
|---|---|---|
| Local SQLite or snapshot is copied | Argon2id, wrapped random vault key, AES-256-GCM | Offline guessing if the master password is weak |
| R2 bucket is copied | Same client-side encryption; private bucket | Object metadata remains visible |
| Snapshot bytes are modified | Entry authentication plus authenticated complete manifest | Objects can still be deleted or hidden |
| `latest.json` is modified | Treat as untrusted; compare it with authenticated internal manifest | False metadata may be shown before unlock |
| Two devices publish concurrently | Immutable snapshots plus conditional pointer update using ETag | User must choose between divergent complete snapshots |
| Timestamp is wrong | Order by snapshot and parent IDs, never timestamps | Displayed time may still confuse the user |
| Download or restore fails halfway | Temporary files, full validation, temporary database, atomic swap | Disk failure may require rollback or independent backup |
| R2 credential is stolen | Credential Manager, bucket restriction, rotation | Attacker may copy or delete ciphertext until revocation |
| R2 is unavailable | Offline-first local SQLite | Remote freshness is unknown |
| R2 replays an old snapshot | Local applied ID and lineage checks | A new device has no trusted checkpoint |
| Clipboard is observed | Explicit copy and timed clearing | Another process may copy it immediately |
| Ordinary unlocked-process memory is captured | Metadata remains plaintext, but unopened secret envelopes remain ciphertext | Vault key in the process can still derive the secret key |
| Unlocked process is controlled by malware | Automatic lock and short secret lifetime | Malware can use the vault key to decrypt every secret |
| Malicious dependency or installer | Minimal dependencies, review, scanning, controlled build | Supply-chain risk cannot be eliminated |

## Cryptographic rules

- Never store “hashed entries”; entries must be encrypted because they need to be recovered.
- Never invent a cipher or modify a standard algorithm.
- Never reuse an AES-GCM nonce with the same key.
- Generate vault keys, recovery keys, salts, snapshot IDs, and nonces with a cryptographic random generator.
- Derive independent keys with HKDF-SHA-256 using exact format contexts `metadata-v1`, `secrets-v1`, and `manifest-v1`.
- Authenticate vault ID, entry ID, envelope kind, and format version as additional data.
- Give metadata and secret envelopes independent fresh nonces and revisions.
- Authenticate a complete snapshot manifest covering both envelopes so valid records cannot be removed or paired differently unnoticed.
- Reject unknown versions, malformed lengths, and authentication failures without partial plaintext.
- Keep callers from selecting algorithms, derivation contexts, keys, or nonces.
- Clear mutable key and plaintext buffers where practical, without claiming guaranteed .NET memory erasure.
- Include known-answer, randomized round-trip, tamper, truncation, record-removal, and wrong-password tests.

## R2 rules

- Use a dedicated private bucket; do not expose public object URLs.
- Use a credential restricted to that bucket and required object operations.
- Store the secret in Windows Credential Manager.
- Use HTTPS and normal certificate validation.
- Upload snapshot objects with unique IDs and `If-None-Match: *`.
- Never rewrite an existing snapshot object.
- Update `latest.json` only with `If-Match` for the previously observed ETag.
- Treat `412 PreconditionFailed` as a conflict, never as permission to retry unconditionally.
- Keep comments nonsensitive.
- Bound metadata and snapshot download sizes.
- Maintain an independent encrypted backup outside R2.

## Desktop behavior

- Start locked.
- Permit offline unlock when R2 is unavailable.
- Display pre-unlock remote comments as **unverified**.
- Decrypt metadata after unlock for search and sorting; leave secret envelopes encrypted.
- Require only title; treat username, email, URL, `d-tags`, `s-tags`, password, and notes as optional.
- Show a fixed `••••••••` password placeholder that does not disclose real length.
- Derive the secret key and decrypt only the selected entry's secret envelope after explicit Reveal, Copy, secret edit, or notes access.
- Hide a revealed password after a short timeout.
- Clear temporary selected-entry secret values and the derived secret key where practical.
- Clear copied passwords after a short configurable interval.
- Disable HTTP body logging and redact R2 authorization information.
- Never replace the working database before authenticating and validating the complete snapshot.
- Preserve a bounded rollback copy before an atomic database swap.
- Show clear states: local changed, remote unchecked, update available, published, and conflicted.

## Accepted simplifications

- Whole-vault replacement instead of record merging.
- One writer at a time instead of simultaneous editing.
- Manual check, restore, and publish instead of background synchronization.
- Direct R2 access instead of Worker/D1.
- Plaintext non-secret pointer metadata instead of pre-unlock device signatures.
- Local lineage checks and backups instead of complete malicious-server rollback prevention.
- No DPAPI convenience unlock in the simplest version; enter the master password each run.

These choices remove substantial identity, API, database, merge, and recovery code. They are acceptable only when the UI clearly communicates remote state and never silently chooses a winner.

## Verification checklist before real use

- [ ] SQLite inspection reveals no entry plaintext
- [ ] `.pvault` inspection reveals no entry plaintext or plaintext vault key
- [ ] Unlocking and searching never decrypts a secret envelope
- [ ] Snapshot creation, remote checking, download, and restore never decrypt secret envelopes
- [ ] Search and sort cover title, username, email, URL, `d-tags`, and `s-tags`
- [ ] Only title is required by validation
- [ ] Metadata and secret envelopes use distinct HKDF contexts and authenticated envelope kinds
- [ ] Reveal and Copy decrypt only the selected entry's secret envelope
- [ ] Password placeholder length never reflects actual password length
- [ ] Reveal timeout hides the password and temporary values are released
- [ ] Wrong password returns no partial plaintext
- [ ] One-bit modifications to header, entries, tags, IDs, and manifest are rejected
- [ ] Removing one valid encrypted record causes snapshot verification failure
- [ ] A malformed `latest.json` never changes local data
- [ ] A wrong SHA-256, size, vault ID, parent, or generation is rejected
- [ ] Database replacement happens only after complete authentication
- [ ] Interrupted restore leaves the old database usable
- [ ] Conditional pointer failure preserves both snapshots
- [ ] R2 outage permits offline unlock and blocks unsafe publication
- [ ] R2 credentials never appear in SQLite, snapshots, logs, or source code
- [ ] Independent encrypted backup restores successfully on a clean machine
- [ ] Old and unreferenced snapshot retention is documented
- [ ] Build dependencies and artifacts are scanned
- [ ] Distribution or self-build origin is controlled and documented
