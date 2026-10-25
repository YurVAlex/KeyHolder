# Step-by-step data flows

This guide follows data through the offline vault and the manual Cloudflare R2 snapshot workflow. The [threat-control matrix](threat-control-matrix.md) explains which controls protect each step and which risks remain.

## 1. Values to distinguish

| Value | Purpose | Where it may exist |
|---|---|---|
| Master password | User knowledge used to derive a key | Password box and process memory during unlock; never stored or uploaded |
| Password key | Argon2id result | Desktop memory briefly |
| Vault key | Random root key for the vault | Desktop memory while unlocked; only wrapped form in SQLite and snapshots |
| Metadata key | `HKDF-SHA-256(vault key, "metadata-v1")` | Desktop memory while unlocked so metadata can be searched and sorted |
| Secret key | `HKDF-SHA-256(vault key, "secrets-v1")` | Derived only for a selected secret operation and cleared where practical afterward |
| R2 access key and secret | Authorize object operations | Windows Credential Manager and signed HTTPS requests; never inside the vault |
| Encrypted snapshot | Portable complete vault state | Temporary local file, independent backups, and private R2 |

The credentials have separate jobs:

```text
R2 credential ──► permission to download encrypted bytes

Master password ──► ability to unwrap the vault key

Vault key ──► derives separate metadata, secret, and manifest keys
```

R2 access alone does not decrypt the vault. The master password alone does not authorize R2 access.

## 2. Entry data forms

```text
entry
  id
  metadata_envelope
    title                 ← only mandatory field
    username
    email
    url
    d-tags
    s-tags
  secret_envelope
    password
    notes
```

```text
Vault key
   ├── HKDF("metadata-v1") ──► metadata key ──► metadata envelope
   └── HKDF("secrets-v1") ───► secret key ────► secret envelope
```

Both envelopes use AES-256-GCM with independent fresh nonces and authenticated envelope types. All listed fields are encrypted in SQLite and R2. R2 sees snapshot sizes, object names, upload times, and the non-secret fields intentionally placed in `latest.json`.

`d-tags` and `s-tags` are separate optional collections of user-defined strings. The app does not infer additional meaning from their names.

## 3. Remote object lineage

Every snapshot points to the snapshot on which it was based:

```text
Snapshot A
  generation: 1
  parent: none
        │
        ▼
Snapshot B
  generation: 2
  parent: A
        │
        ▼
Snapshot C
  generation: 3
  parent: B
```

Timestamps do not establish this order. Snapshot IDs and parent IDs do.

---

## Scenario A — creating the first local vault

1. The user selects **Create vault** and enters the master password twice.
2. The app generates a random vault ID and Argon2id salt.
3. Argon2id derives a password key from the password, salt, and saved cost settings.
4. The app generates an independent random 256-bit vault key.
5. AES-GCM wraps the vault key with the password key.
6. If recovery is enabled, a random recovery key creates a second wrapping of the same vault key.
7. SQLite stores the salt, KDF settings, wrapped vault key, and empty entry set.
8. The master password and password key are cleared as soon as practical.
9. The vault remains unlocked with the vault key in process memory.
10. No network connection or R2 account is required.

```text
Master password
      │
      ▼
Argon2id ──► password key
                  │
Random vault key ├──► wrapped vault key ──► SQLite
                  │
                  └──► encrypt entries later
```

---

## Scenario B — unlocking the local vault

1. The app reads the vault header from SQLite.
2. The user enters the master password.
3. Argon2id derives a candidate password key using the stored salt and settings.
4. AES-GCM attempts to unwrap the vault key.
5. If authentication fails, the app reports a generic unlock failure and returns no plaintext.
6. If successful, the app keeps the vault key in memory for the session.
7. HKDF-SHA-256 derives the metadata key using the exact context `metadata-v1`.
8. Every metadata envelope is authenticated and decrypted into the in-memory search model.
9. Secret envelopes remain ciphertext.
10. Search and sorting happen on title, username, email, URL, `d-tags`, and `s-tags` in memory, not through a plaintext database index.

The master password never goes to R2.

---

## Scenario C — creating or editing an entry

1. The user enters or changes fields and selects **Save**.
2. Domain validation requires a non-empty title and checks all field, tag, and collection size limits.
3. Metadata and secret objects are serialized separately.
4. A metadata edit uses the metadata key and a fresh nonce to replace `metadata_envelope`.
5. A password or notes edit derives the secret key, uses a separate fresh nonce, and replaces `secret_envelope`.
6. Vault ID, entry ID, envelope kind, and schema version are authenticated as additional data.
7. SQLite begins a transaction.
8. Changed envelopes and their independent revisions are saved.
9. `local_changed_since_snapshot` becomes true in the same transaction.
10. SQLite commits.
11. Temporary plaintext and secret-key buffers are cleared where practical.

An unchanged secret envelope is not decrypted or re-encrypted during a metadata-only edit.

There is no automatic upload. The UI shows that unpublished local changes exist.

```text
Saved locally:       Yes
Published to R2:     No
Last remote check:   5 minutes ago
```

---

## Searching, selecting, revealing, and copying

### Search and selection

1. The user searches or sorts the already decrypted metadata model.
2. Results may use title, username, email, URL, `d-tags`, and `s-tags`.
3. The user selects an entry.
4. The app displays that entry's metadata.
5. The secret envelope remains encrypted.
6. The password box displays the fixed placeholder `••••••••`.

The placeholder always has the same length. It does not reveal whether the real password is empty, short, or long.

### Reveal flow

```text
1. User selects an entry
2. App displays decrypted metadata
3. Password remains encrypted
4. User clicks Reveal
5. App derives the secret key and decrypts only the selected secret envelope
6. Password is displayed briefly
7. App hides it after a timeout
8. Temporary plaintext and secret-key buffers are cleared where practical
```

Because password and notes share one secret envelope, both values exist briefly in the selected secret payload even when only the password is shown. No other entry's secret envelope is decrypted.

### Copy flow

```text
Click Copy
    ↓
Derive secret key and decrypt selected secret envelope
    ↓
Place only the password on the clipboard
    ↓
Clear temporary secret payload and derived key where practical
    ↓
Clear the owned clipboard value after timeout
```

Reveal or Copy without another password prompt is lazy decryption, not a separate cryptographic lock: the vault key in the process can derive the secret key. This design reduces accidental plaintext exposure but does not stop malware controlling the unlocked process.

---

## Scenario D — starting the app and checking R2

This check occurs before normal unlock if R2 is configured.

1. The app reads the locally applied snapshot ID and generation without decrypting entry content.
2. It retrieves `latest.json` and its ETag over HTTPS.
3. It rejects excessive size, invalid JSON, wrong vault ID, unsupported version, or malformed IDs.
4. It compares the remote snapshot ID and parent ID with local state.
5. It displays a resulting state.

```text
                 Fetch latest.json
                        │
          ┌─────────────┼─────────────┐
          ▼             ▼             ▼
      Same ID      Remote child    Divergence
          │             │             │
     Unlock local   Offer update   Stop and warn
```

Remote comment and timestamp are labeled **unverified** because the vault key has not authenticated them yet.

---

## Scenario E — local and remote snapshots match

1. `latest.json.snapshotId` equals `remote_state.applied_snapshot_id`.
2. The app may save the newly observed pointer ETag.
3. No snapshot is downloaded.
4. The normal master-password unlock proceeds.

An equal ID matters more than timestamps. A cloud upload time can differ without indicating different vault content.

---

## Scenario F — R2 is unavailable

1. The metadata request fails because the device is offline, credentials are invalid, R2 is unavailable, or a timeout occurs.
2. The app does not modify local data.
3. It shows:

```text
Remote status could not be checked.
You may open the local vault offline.

[Open local vault] [Retry]
```

4. If the user continues, the normal local unlock runs.
5. Publishing remains disabled until the remote state is checked again.

Opening an existing local vault must never depend on cloud availability.

---

## Scenario G — a direct remote successor is found

Assume local snapshot is B and the pointer says remote snapshot C has parent B.

```text
Local:  B
Remote: C, parent B
```

The app displays:

```text
An unverified newer remote snapshot is available.

Device: Home laptop
Comment: Updated from home laptop
Generation: 12
Created: 5 October 2026, 19:20 UTC

[Download and verify] [Open local without updating]
```

The comment helps the user recognize expected activity, but it does not prove authenticity.

---

## Scenario H — downloading and applying an approved remote snapshot

1. The user selects **Download and verify**.
2. The app downloads `snapshots/{snapshotId}.pvault` to a newly created temporary file.
3. Download size is bounded.
4. The app checks the byte length and SHA-256 shown in `latest.json`.
5. It asks for the master password.
6. Argon2id derives the password key from the snapshot header.
7. The password key unwraps the vault key.
8. The vault key derives the manifest key, which authenticates the internal manifest.
9. The app verifies:
   - snapshot ID;
   - parent snapshot ID;
   - generation;
   - vault ID;
   - complete record list;
   - every metadata and secret envelope's identity, structure, and ciphertext hash.
10. The external comment and other pointer fields must match the authenticated internal values.
11. A new temporary SQLite database is created from the validated content.
12. The app opens that database and runs migrations and consistency checks.
13. The current local database is copied to a bounded rollback location.
14. All current database connections are closed.
15. The temporary database atomically replaces the working database.
16. Local remote state records the applied snapshot.
17. The metadata key is derived, metadata envelopes are authenticated and decrypted for search, and the vault opens.
18. Secret envelopes are not decrypted during download, snapshot verification, restore, or ordinary unlock.

```text
R2 snapshot
     │ download
     ▼
Temporary file
     │ password + cryptographic verification
     ▼
Temporary SQLite
     │ schema and consistency checks
     ▼
Rollback old DB + atomic replacement
     │
     ▼
Unlocked updated vault
```

The current database is never overwritten before the downloaded snapshot is authenticated.

---

## Scenario I — the remote snapshot is rejected

Rejection occurs if the password is wrong or the file is truncated, malformed, modified, from another vault, or inconsistent with the pointer.

1. Verification stops immediately.
2. No plaintext from an unauthenticated record is returned.
3. The local database remains unchanged.
4. The temporary file is removed or quarantined without exposing content.
5. The app shows a nonsensitive reason such as:

```text
The remote snapshot could not be authenticated.
Your local vault was not changed.
```

6. The user can open the local vault, retry, inspect another snapshot, or restore an independent backup.

---

## Scenario J — publishing local changes

Publishing happens only after an explicit user action.

1. The user unlocks the vault and selects **Publish snapshot**.
2. The app fetches `latest.json` and remembers its ETag.
3. It confirms the remote snapshot still equals the locally applied base.
4. If not, publishing stops before upload and reports a conflict.
5. The app briefly blocks writes and creates a consistent SQLite backup or record export.
6. It creates:
   - new random snapshot ID;
   - parent ID equal to remote latest;
   - generation equal to parent generation plus one;
   - device name and non-secret comment.
7. It creates and authenticates the internal manifest.
8. It writes, reopens, and validates the complete temporary `.pvault`.
9. It uploads the snapshot using `If-None-Match: *`.
10. It prepares a matching `latest.json`.
11. It replaces the pointer using `If-Match` with the ETag from step 2.
12. R2 accepts the pointer only if nobody changed it in the meantime.
13. The app retrieves the pointer again and checks its snapshot ID.
14. Local state is marked published.

```text
Local SQLite
     │ consistent export
     ▼
New immutable snapshot D
     │ PUT If-None-Match: *
     ▼
R2 snapshots/D.pvault
     │
     ▼
PUT latest.json If-Match: previous ETag
```

---

## Scenario K — another device publishes during publication

Suppose both devices fetched pointer B.

```text
Device 1 uploads C and points latest to C

Device 2 uploads D
Device 2 tries: replace latest only if it is still B
Current latest is C
        ↓
R2 returns 412 PreconditionFailed
```

1. Device 2 does not retry by overwriting the pointer.
2. Its uploaded D snapshot remains an unreferenced but valid object.
3. The app keeps local data and marks publication as conflicted.
4. The user must inspect C and decide whether to keep local D or restore remote C.
5. Version 1 does not merge C and D.

This is the main reliability purpose of the ETag condition.

---

## Scenario L — divergence is found at startup

```text
             ┌── remote C
base B ──────┤
             └── local unpublished D
```

1. The app sees that the remote pointer no longer refers to the local base.
2. It also sees local unpublished changes.
3. It disables ordinary publish and automatic replacement.
4. It offers:

```text
Both local and remote versions contain changes.

[Inspect remote metadata]
[Use remote and preserve local rollback]
[Keep local without publishing]
[Cancel]
```

5. No timestamp determines the winner.
6. Whichever complete snapshot is not selected remains preserved until the user deliberately removes it.

Field-by-field merge is omitted because it would require per-entry base versions, deletion tracking, and conflict resolution equivalent to a synchronization engine.

---

## Scenario M — configuring the first R2 device

1. The user creates a private R2 bucket and a bucket-restricted API credential.
2. In app settings, the user enters:
   - R2 endpoint/account information;
   - bucket name;
   - access key ID;
   - secret access key.
3. The app tests a bounded metadata request.
4. Windows Credential Manager stores the access key and secret.
5. SQLite stores only nonsensitive R2 configuration and remote snapshot state.
6. The user publishes the first snapshot.
7. Because `latest.json` does not exist, the app creates it with `If-None-Match: *`.

The bucket remains private. There is no public object URL.

---

## Scenario N — configuring another device

1. The user securely configures the same R2 endpoint, bucket, and credentials on the new device.
2. The app fetches `latest.json`.
3. With no local vault, it offers initial download.
4. The encrypted snapshot is downloaded to a temporary file.
5. The user enters the master password.
6. The app authenticates and restores it through Scenario H.
7. The second device is ready, but the single-writer rule still applies.

Copying R2 credentials between devices authorizes both devices to change objects. The app cannot revoke only one device when they share one access key. Separate bucket-scoped credentials per device are preferable if R2 account management supports the required separation.

---

## Scenario O — changing the master password

1. The unlocked app verifies the current password.
2. It generates a new Argon2id salt.
3. The new password derives a new password key.
4. That key creates a new wrapping of the same vault key.
5. SQLite atomically replaces the vault-header wrapping.
6. Local state becomes unpublished.
7. The next published snapshot contains the new wrapping.

Entries do not need re-encryption.

Old immutable R2 snapshots still contain the old wrapping. Someone possessing an old snapshot and the old password may still obtain the vault key. Password change is not retroactive protection for historical snapshots.

---

## Scenario P — losing or rotating an R2 credential

1. The user creates a replacement bucket-restricted credential in Cloudflare.
2. The app tests it without deleting the old credential.
3. Credential Manager stores the replacement.
4. The app confirms it can read the pointer and upload to the expected bucket.
5. The old credential is revoked in Cloudflare.

Revocation stops future R2 operations using that credential. It does not erase snapshots already downloaded by another person.

If the only R2 credential is lost, the local vault still opens offline. The user can configure new remote storage and publish a fresh snapshot.

---

## Scenario Q — locking and clipboard handling

When the user locks, becomes idle, locks Windows, or suspends the computer:

1. The app closes entry detail views.
2. It removes plaintext metadata, any temporary secret payloads, and vault-derived keys from active models.
3. Mutable buffers are cleared where practical.
4. Cryptographic objects are disposed.
5. A clipboard value placed by the app is cleared if it is still present.
6. The lock screen is shown.

Reveal and Copy follow the earlier lazy-decryption flows. The app clears its clipboard value after a short delay, but cannot undo a copy already made by another process or Windows clipboard history.

---

## Scenario R — independent encrypted backup and restore

R2 is not the only backup.

1. The user creates a validated `.pvault` export.
2. It is copied to an independent location such as an encrypted removable drive.
3. Restore follows the same authenticate-to-temporary-database process as an R2 snapshot.
4. R2 configuration is not automatically changed.
5. The user chooses whether to publish the restored state as a new snapshot.

At least one restore drill should be performed before using the vault for important data.

---

## Scenario S — malicious or compromised R2 storage

An attacker controlling R2 may:

- read ciphertext and untrusted metadata;
- replace `latest.json`;
- hide or delete snapshots;
- replay an old valid snapshot;
- upload malformed objects;
- deny service.

The attacker should not be able to decrypt content without the master password or recovery key. The app detects modified ciphertext and unexpected lineage, preserves local data before replacement, and can operate offline.

A new device has no trusted previous snapshot ID, so a fully malicious service may present a consistent old snapshot. Independent backups and user-recognizable history reduce this risk; completely preventing it requires another trust source and is outside version 1.

## 4. Core invariants to test

1. Master passwords and plaintext vault keys are never serialized, logged, or uploaded.
2. Metadata and secrets use distinct HKDF-derived keys with exact, versioned context strings.
3. Metadata and secret envelopes have independent fresh nonces and authenticated envelope kinds.
4. Unlock decrypts metadata but leaves every secret envelope encrypted.
5. Reveal and Copy decrypt only the selected entry's secret envelope and use a fixed password placeholder.
6. Every snapshot has a random ID, parent ID, and authenticated complete manifest covering both envelopes.
7. Timestamps never decide snapshot ordering.
8. Remote metadata is untrusted until it matches the authenticated internal manifest.
9. Downloads never replace the local database before full validation.
10. Restores use a new temporary database and preserve a rollback copy.
11. Snapshot objects are immutable.
12. Pointer replacement uses the previously observed ETag.
13. `412 PreconditionFailed` creates a conflict and never triggers unconditional overwrite.
14. R2 failure never prevents offline local unlock.
15. Local changes are never described as published until the pointer is read back successfully.
