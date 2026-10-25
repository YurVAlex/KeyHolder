# Personal Secrets Vault

This repository defines the architecture for a small Windows password vault with optional, user-approved remote snapshots in Cloudflare R2.

> **Important:** this is a learning project, not yet a safe replacement for a mature password manager. Use fake data until encryption, backup/restore, R2 conflict handling, and release security have been tested and independently reviewed. Keep real credentials in an established product such as Bitwarden, 1Password, or KeePassXC.

## Chosen architecture

| Part | Choice | Purpose |
|---|---|---|
| Desktop UI | .NET 10 LTS, C#, WPF, MVVM | Windows-native personal application |
| Local working data | SQLite | Transactional offline vault |
| Protection | Argon2id, a random vault key, HKDF-SHA-256, and AES-256-GCM | Separately encrypt searchable metadata and secret fields before disk or cloud |
| Remote storage | Private Cloudflare R2 bucket through its S3-compatible API | Store immutable encrypted vault snapshots |
| Remote credentials | Windows Credential Manager | Protect the bucket access key on the Windows device |
| Tests | xUnit plus R2 protocol integration tests | Verify cryptography, snapshot restore, and conflict behavior |

There is no Worker, D1, KV, Git repository synchronization, background synchronization, or server-side database. R2 stores files and cannot decrypt them.

## Entry model

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

Both envelopes are encrypted at rest. Unlocking derives separate metadata and secret keys from the vault key:

```text
Vault key
   ├── HKDF("metadata-v1") ──► metadata key
   └── HKDF("secrets-v1") ───► secret key
```

The app decrypts metadata after unlock for searching and sorting. Passwords and notes remain encrypted until needed. The password box always shows the fixed placeholder `••••••••`; it does not expose the real password length.

```text
Click Reveal or Copy
        ↓
Decrypt selected entry's secret envelope temporarily
        ↓
Display briefly or place password on clipboard
        ↓
Hide display and clear temporary value
        ↓
Clear owned clipboard value after timeout
```

## Remote workflow

```text
App starts
    ↓
Fetch untrusted remote metadata from R2
    ↓
Compare snapshot ID, parent ID, and generation
    ↓
User approves a newer snapshot
    ↓
Download to a temporary file
    ↓
Enter master password and authenticate the snapshot
    ↓
Restore into a new temporary SQLite database
    ↓
Atomically replace local database and unlock
```

After local changes, the user explicitly chooses **Publish snapshot**. The app uploads a new immutable encrypted file, then conditionally updates `latest.json` using the previous R2 ETag. A failed condition means another device published first; the app reports a conflict and overwrites nothing.

Timestamps and comments are display hints, not proof that a snapshot is newer. Whole-vault replacement is used instead of field-level merging. The intended first version follows a single-writer rule: fetch before editing, publish after editing, and never edit concurrently on two devices.

## Documents

- [Architecture](docs/architecture.md) — system structure, snapshot format, R2 object layout, and update protocol
- [Step-by-step data flows](docs/data-flows.md) — local use, remote checks, publishing, restoring, and conflicts
- [Security model](docs/security-model.md) — assets, threats, limitations, and verification checklist
- [Threat-control matrix](docs/threat-control-matrix.md) — plain-language controls mapped to threats and deliberate simplifications
- [Implementation roadmap](docs/roadmap.md) — learning milestones and acceptance criteria

## Solution layout

```text
PersonalVault.sln
Directory.Build.props           # shared analyzers, nullable, lock files, warnings as errors
global.json                     # pinned .NET SDK
.github/workflows/ci.yml        # Windows restore/build/test
src/
  PersonalVault.Domain/          # entities and business rules
  PersonalVault.Cryptography/    # narrow, reviewed encryption boundary
  PersonalVault.Infrastructure/  # SQLite, snapshots, R2, Credential Manager
  PersonalVault.Desktop/         # WPF views and view models
tests/
  PersonalVault.UnitTests/
  PersonalVault.IntegrationTests/
docs/
```

Build and prove the offline vault first. Add encrypted export/restore second and direct R2 publication only after local recovery is reliable.

## Development

Milestone 0 is implemented. The application currently launches as a locked shell and deliberately does not accept a master password or store data.

Requirements:

- Windows 11;
- .NET 10 SDK matching [`global.json`](global.json) (currently 10.0.402, with feature-band roll-forward);
- Visual Studio 2026 with the .NET desktop development workload, **or** VS Code / Cursor with the C# Dev Kit extension, **or** the `dotnet` CLI alone.

### CLI

```powershell
dotnet restore PersonalVault.sln
dotnet build PersonalVault.sln --configuration Release --no-restore
dotnet test PersonalVault.sln --configuration Release --no-build --no-restore
dotnet run --project src/PersonalVault.Desktop/PersonalVault.Desktop.csproj
```

On CI hosts the restore step uses `--locked-mode` so package versions must match the committed `packages.lock.json` files.

The solution can be restored and compiled from non-Windows hosts because the desktop project enables Windows targeting, but the WPF application itself runs only on Windows.

### Tests in VS Code / Cursor

1. Install the recommended extensions when prompted (C# Dev Kit, C#, .NET Install Tool). Or open the Extensions view and search for the IDs listed in [`.vscode/extensions.json`](.vscode/extensions.json).
2. Open this folder as the workspace root so `PersonalVault.sln` loads (see `.vscode/settings.json`).
3. Open the **Testing** view (beaker icon in the Activity Bar). After the solution finishes loading, expand `PersonalVault.UnitTests` and `PersonalVault.IntegrationTests`, then run all tests or a single test with the play buttons.
4. Or run the default test task: **Terminal → Run Task… → test** (or **Run Test Task**). That builds Debug and runs `dotnet test` for the whole solution.
5. To debug a test: open the test method, set a breakpoint, then use **Debug Test** above the `[Fact]` attribute (CodeLens) or the debug icon in the Testing view.

Launch the locked shell with **Run and Debug → PersonalVault.Desktop**, or `F5` after selecting that configuration.

### CI workflow

The workflow lives at [`.github/workflows/ci.yml`](.github/workflows/ci.yml). It restores with locked packages, builds Release, and runs all tests on `windows-latest`.

**Validate the same commands locally (fastest feedback):**

```powershell
$env:CI = "true"
dotnet restore PersonalVault.sln --locked-mode
dotnet build PersonalVault.sln --configuration Release --no-restore
dotnet test PersonalVault.sln --configuration Release --no-build --no-restore --verbosity normal
```

Or run the VS Code / Cursor task **ci**, which sets `CI=true` and runs the Release test path.

**Validate through GitHub Actions:**

1. Push the branch (or open a PR) to a GitHub remote. The workflow runs on `push` to `main`/`master`, on pull requests, and via **Actions → CI → Run workflow** (`workflow_dispatch`).
2. From a machine with the GitHub CLI: `gh run list --workflow=ci.yml` then `gh run watch`.
3. Optional local runner simulation with [act](https://github.com/nektos/act): because this job uses `windows-latest` and WPF, prefer host mode rather than a Linux container — `act -P windows-latest=-self-hosted -W .github/workflows/ci.yml`. That still depends on act/Node being installed and is a looser approximation than GitHub’s Windows image; prefer the CLI commands above for day-to-day checks.

Project dependencies point inward:

```text
Desktop ──► Infrastructure ──► Domain
                    │
                    └────────► Cryptography

Domain and Cryptography do not reference UI or infrastructure.
```

[`Directory.Build.props`](Directory.Build.props) enables nullable reference types, recommended .NET analyzers, deterministic builds, code-style enforcement, package lock files, and warnings as errors. [`.editorconfig`](.editorconfig) supplies formatting and IDE analyzer severity.
