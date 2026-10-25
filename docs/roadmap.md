# Implementation and learning roadmap

Each milestone produces a small result that can be tested independently. Start offline; R2 is the final storage adapter, not the foundation of the application.

## Milestone 0 — solution skeleton

**Status: implemented.** The repository contains the layered .NET 10 solution, dependency-injected locked WPF shell, architecture smoke tests, shared quality settings, package lock files, and Windows CI. Unlock and data-storage behavior remain intentionally disabled.

Learn: project references, dependency injection, unit tests, analyzers, and GitHub workflows.

Build:

- .NET 10 solution with Domain, Cryptography, Infrastructure, Desktop, and test projects;
- WPF shell with lock/unlock screens;
- nullable reference types, formatting, analyzers, and warnings as errors;
- CI build and test workflow.

Done when a clean Windows environment can clone, build, test, and launch the locked shell.

## Milestone 1 — local vault with fake data

Learn: MVVM, commands, validation, repositories, and SQLite transactions.

Build:

- entry model with mandatory title and optional username, email, URL, `d-tags`, `s-tags`, password, and notes;
- create, edit, list, search, sort, and delete entries;
- SQLite repository and migration mechanism;
- password generator;
- idle and Windows-session locking;
- fixed `••••••••` password placeholder;
- explicit Reveal and Copy with timeouts;
- visible local dirty state.

Use obviously fake credentials. Hide persistence behind interfaces so the crypto format is not accidentally defined by the UI.

Done when fake CRUD data survives restart and domain behavior is unit tested.

## Milestone 2 — encrypted local vault

Learn: threat modeling, password key derivation, authenticated encryption, key wrapping, and binary serialization.

Build:

- separate `metadata_envelope` and `secret_envelope` columns;
- Argon2id password-key derivation;
- random vault key;
- HKDF-SHA-256 key separation using exact `metadata-v1`, `secrets-v1`, and `manifest-v1` contexts;
- AES-256-GCM encryption with independent fresh nonces for each envelope;
- authenticated vault, entry, envelope-kind, and version context;
- metadata unlock for title, username, email, URL, `d-tags`, and `s-tags`;
- lazy selected-entry secret decryption for password and notes;
- Reveal timeout and selected-entry Copy flow;
- password-change rewrapping;
- optional recovery-key wrapping;
- wrong-password, envelope-kind swap, tamper, lazy-decryption, and randomized round-trip tests.

Done when SQLite inspection reveals no metadata, passwords, or notes; searching never decrypts secret envelopes; Reveal/Copy decrypts only the selected entry's secret envelope; and every altered protected field is rejected.

Pause for review of the envelope and key lifecycle before proceeding.

## Milestone 3 — authenticated snapshot export

Learn: consistent SQLite backup, streaming I/O, complete manifests, atomic files, and bounded parsing.

Build:

- `.pvault` container with header, both envelopes for every entry, and authenticated complete manifest;
- random snapshot ID, parent ID, generation, device name, and comment;
- export through SQLite backup or record-level transaction;
- temporary write, reopen, validate, and atomic move;
- SHA-256 calculation for exact snapshot bytes.

Done when truncation, mutation, envelope removal or re-pairing, duplicate IDs, unknown versions, and unreasonable sizes all fail safely.

## Milestone 4 — staged restore and rollback

Learn: failure recovery, database replacement, migrations, and rollback.

Build:

- parse and authenticate snapshot before replacement;
- restore into a new temporary SQLite database;
- schema and consistency checks;
- bounded rollback copy of current database;
- atomic database swap;
- startup cleanup of abandoned temporary files;
- independent encrypted export restore.

Done when injected failures at every restore step leave either the old or fully restored database usable, never a partial database.

## Milestone 5 — read-only R2 integration

Learn: S3-compatible APIs, AWS Signature Version 4 through a maintained .NET client, HTTPS, ETags, Credential Manager, and timeouts.

Build:

- private bucket configuration;
- bucket-restricted access credentials stored in Credential Manager;
- `latest.json` retrieval and strict validation;
- ETag capture;
- state comparison using snapshot/parent IDs;
- offline fallback when R2 cannot be reached;
- bounded snapshot download to temporary storage.

Use the latest stable compatible S3 client package when implementation begins and pin it through the .NET lock mechanism.

Done when the app correctly reports same, direct successor, missing local vault, malformed pointer, unavailable R2, and divergent state without modifying local data.

## Milestone 6 — user-approved remote update

Learn: trust boundaries and staged verification.

Build:

- unverified remote metadata prompt;
- master-password verification of downloaded snapshot;
- comparison between pointer and authenticated internal manifest;
- full staged restore from R2;
- clear cancel and failure paths;
- preserved local rollback.

Done when no R2-controlled input can replace SQLite before full cryptographic and structural validation.

## Milestone 7 — explicit R2 publication

Learn: immutable objects, optimistic concurrency, conditional requests, and ambiguous network outcomes.

Build:

- consistent local snapshot creation;
- immutable upload using `If-None-Match: *`;
- conditional `latest.json` replacement using the observed ETag and `If-Match`;
- `412 PreconditionFailed` conflict handling;
- read-after-write pointer confirmation;
- safe handling of uploaded but unreferenced snapshots;
- published/dirty/conflicted status UI.

Done when two test clients publishing from the same parent preserve both snapshots and exactly one pointer update succeeds.

## Milestone 8 — operational hardening

Learn: credential rotation, supply-chain controls, disaster recovery, and security review.

Build:

- R2 credential replacement and revocation procedure;
- independent encrypted backup schedule and restore drill;
- conservative snapshot retention and cleanup policy;
- dependency and secret scanning;
- controlled self-build or signed installer;
- fuzz/property tests for snapshot parser and lineage state machine;
- external security review with findings fixed or explicitly accepted.

Only after this milestone should limited real credentials be considered.

## Recommended first issues

1. Create the .NET solution and dependency rules.
2. Define `VaultEntry` and validation tests.
3. Define `IEntryRepository` and an in-memory implementation.
4. Build the locked WPF shell with fake entries.
5. Add SQLite migrations and transaction tests.
6. Write the encrypted-envelope binary specification.
7. Implement and review local encryption.
8. Specify `.pvault` before writing R2 code.

## Architecture decisions to record

Create short Architecture Decision Records for:

- WPF versus WinUI 3;
- per-entry encryption instead of only full-file encryption;
- immutable R2 snapshots instead of Worker/D1 or Git;
- snapshot lineage instead of timestamp ordering;
- whole-vault replacement instead of field merging;
- direct bucket credentials and their storage;
- single-writer manual publication;
- installer and update mechanism.
