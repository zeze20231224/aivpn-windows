# Code signing policy

Status: preparation only. SignPath has not accepted this project, provided a subscription, or signed any AIVPN release.

Proposed maintainer, reviewer and release approver: GitHub account zeze20231224. The account owner must confirm these roles and enable/verify MFA on GitHub and SignPath before applying.

Only AIVPN-owned client, service and installer binaries built on GitHub-hosted Windows runners from the published source repository may be submitted. Upstream executables and DLLs must retain upstream signatures and licenses; they must never be re-signed as AIVPN-owned code.

Every release signing request requires manual approval. The release must use matching product and file versions. The signed payload must be re-hashed before creating the upgrade installer; the final installer signature must also be verified before updating website hashes.

If accepted, project and release pages will include the required attribution: Free code signing provided by SignPath.io, certificate by SignPath Foundation. No current sponsorship or approval is claimed.

See PRIVACY.md for user data processing. Acceptance of Wintun's prebuilt binary license and system-library treatment of Microsoft components remains an issuer-review question.
