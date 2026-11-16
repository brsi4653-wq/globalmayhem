# Global Mayhem

This repo holds two things:

- **The Global Mayhem site** (https://globalmayhem.org): one page that presents Global Mayhem and our product, OnRecord, plus a 404 page.
- **OnRecord**, in the [`onrecord`](onrecord) folder: the app's source code. It has its own [README](onrecord/README.md) and [LICENSE](onrecord/LICENSE). Installers are published on this repo's [Releases](https://github.com/brsi4653-wq/globalmayhem/releases) page.

The site as it was before OnRecord is kept under the tag `archive-before-onrecord`.

## The site

Built with Eleventy (Node 22). Only the `src` folder is built. The `onrecord` folder is outside it and is never published as part of the site.

| File | What it is |
|---|---|
| `src/index.njk` | The home page |
| `src/404.njk` | The page shown for any address that does not exist |
| `src/_data/site.js` | Name, description, the OnRecord links, effect switches |
| `src/_includes/layouts/base.njk` | Header, footer and page head |
| `src/assets/css/site.css` | The base look |
| `src/assets/css/theme-sunset.css` | The sunset theme (steps to remove it are at the top of the file) |
| `src/assets/js/fx.js` | Motion effects |
| `src/assets/logo/` | Logo and favicon files |
| `lib/og-card.js` | Makes the link preview image |

Build it:

```bash
npm ci
npx eleventy
```

The finished site is written to `_site/`.

## The download button

The button links to `https://github.com/brsi4653-wq/globalmayhem/releases/latest/download/OnRecord-Setup.exe`. That address always gives the installer from the newest release, so the site does not change when a new version comes out. The version and checksum under the button are read from GitHub when the page opens.

To put out a new OnRecord version: create a new release on this repo with the tag `onrecord-v<version>` and upload the installer as an asset named exactly `OnRecord-Setup.exe`.

## Publishing

The site is hosted on Cloudflare Pages (project `globalmayhem`) by direct upload. GitHub only stores the files. Pushing does not publish anything.

```bash
CLOUDFLARE_API_TOKEN="<token>" CLOUDFLARE_ACCOUNT_ID="<account id>" ./scripts/deploy.sh
```

Add `DEPLOY_BRANCH=preview` in front to upload a private test copy to https://preview.globalmayhem.pages.dev and leave the real site alone. Never save the token in this repo.
