# Security policy

## Versions

Security fixes are focused on the latest published release, including preview releases. Please check whether a problem reproduces on that version and include the exact version or source commit in your report. Older releases do not receive routine backports. This is a community-maintained preview project without a guaranteed response or fix schedule.

## Report a vulnerability privately

Use [GitHub's private vulnerability reporting form](https://github.com/drkreis/ac-server-manager/security/advisories/new). It is also available through **Security** / **Security and quality** → **Report a vulnerability** on this repository.

Do not publish exploit details in an issue, pull request or public comment before the problem has been assessed. If the private form is unavailable, open an issue asking only for a private security contact; leave the vulnerability details out.

A useful private report includes:

- The affected version or commit, Windows version and installation type.
- A description of the impact and any conditions needed to trigger it.
- Minimal reproduction steps using a disposable server and harmless sample files.
- What you expected to happen and what actually happened.
- Relevant reviewed, redacted diagnostics and a proposed fix if you have one.

Do not include actual passwords, API tokens, player identities or private server content. Reproduce destructive file-operation problems against temporary copies, not live servers or someone else's system.

The maintainer will assess reports as availability allows and coordinate next steps through the private advisory. A report may need more information to reproduce. Publication and any advisory credit should be discussed as part of that process.

## Scope

Relevant reports include unintended file access, path traversal, unauthorized overwrite/deletion, unsafe archive extraction, unintended process control and exposure of credentials by the manager.

Ordinary startup problems, incorrect metadata, interface bugs and feature requests belong in [Issues](https://github.com/drkreis/ac-server-manager/issues/new/choose). Conduct concerns use the [Code of Conduct](CODE_OF_CONDUCT.md) reporting route.

AssettoServer, Content Manager, Assetto Corsa, .NET and third-party mods are separate projects. Report a vulnerability in those components to their maintainers; explain any relevant interaction with this manager. This policy does not authorize testing third-party or live systems.
