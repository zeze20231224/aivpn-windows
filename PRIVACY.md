# Client network and data disclosures - draft for owner review

AIVPN authenticates to AI WorkPort and its AIVPN API to obtain device credentials, check device connection/traffic and account usage. Login is initiated by the user; connecting the VPN starts periodic status and usage requests. Device profiles and tokens are stored with Windows DPAPI outside the application install directory.

When a VPN connection is requested, the service creates the AIVPN virtual interface, changes routing and DNS for that connection, and starts the Mihomo engine. Automatic and global routing modes differ. Cloudflare and Alibaba DNS and HTTP connectivity-check endpoints are used as specified in the source. The exact endpoints are in Common.cs and Service.cs.

Closing the client after confirmation or using Disconnect stops the tunnel. Uninstall instructions are in README.md. Encrypted user credentials are retained by the existing uninstaller.

This document describes the reviewed client code only. It is not an approved privacy policy for the remote AI WorkPort/AIVPN services. The owner must confirm the server-side data handling, retention, third-party policies and public privacy-policy URL before SignPath submission. No claim is made that telemetry is absent or that Foundation privacy requirements are already satisfied.
