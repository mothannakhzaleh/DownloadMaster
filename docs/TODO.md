# DownloadMaster — future work

## Planned

### MEGA.nz download support
- **Status:** Not started  
- **Why:** MEGA uses encrypted API downloads, not plain HTTP. The `#key` in sharing links is required client-side for decryption.  
- **Approach:** Integrate [MegaApiClient](https://www.nuget.org/packages/MegaApiClient/) (or similar) as a separate `DownloadKind.Mega` path in the download queue — not an extension of the Files tab HTTP engine.  
- **Notes:** Free MEGA accounts have ~5 GB/day download limits; library is in passive maintenance.
