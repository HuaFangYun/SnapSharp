# NimCap Screenshot website

The public site targets **https://jietu.nimcap.com/**. Chinese pages use the root path; English pages use `/en/`. It contains
Chinese and English product pages, a setup guide, and three workflow guides.
Content is rendered into HTML at build time; navigation, articles, screenshots,
FAQ answers, and download links do not depend on JavaScript. There are no remote
fonts, analytics, cookies, or third-party frontend dependencies.

## Build and preview

Requires Python 3.12+; no packages are needed for the site build.

```powershell
python scripts/build-site.py
python scripts/check-site.py .artifacts/pages
```

For a local preview at the same root path as production, use:

```powershell
python scripts/build-site.py --base-url http://localhost:8080 --output .artifacts/site-preview
python -m http.server 8080 --directory .artifacts/site-preview
```

Then open **http://localhost:8080/**. The output directory is ignored by
Git. Production canonical links and sitemap entries use the production base URL;
do not upload a local-preview build to the public site.

## Publish with GitHub Pages

1. Push the website files and `.github/workflows/pages.yml` to `main`.
2. In repository **Settings → Pages → Build and deployment**, choose
   **GitHub Actions** as the source.
3. Run the **Website** workflow (or push a website change). Its build validates
   the generated site before deploying it to the `github-pages` environment.

The workflow reads the actual Pages URL using `configure-pages`, so canonical
links and asset paths also work with a custom domain. Pull requests build and
validate the site without deploying. If adding a domain, configure it in Pages
settings and keep DNS / HTTPS settings consistent. The repository's Pages
configuration is authoritative for this Actions deployment; a source CNAME
file is not required.

### Custom domain: jietu.nimcap.com

Configure `jietu.nimcap.com` in this repository's **Settings → Pages** and add
a `jietu` CNAME pointing to `nimcap.github.io` at your DNS provider. Enable
HTTPS when GitHub provisions the certificate. This single site serves Chinese
at `/` and English at `/en/`; language switches link to the matching page.
The workflow builds, checks and deploys both languages together. No second
hosting site or domain is needed. DNS and Pages settings must be configured
separately from these source files.

## Edit content and appearance

- `content.py`: Chinese/English editorial pairs, feature descriptions, FAQs, and
  guides. Chinese is first and English is second in each pair.
- `homepage.py`: original landing-page composition, local-AI stories, and
  illustrative annotation / draggable-pin demos.
- `templates/base.html`: semantic page shell and metadata.
- `assets/site.css`: responsive layout and reduced-motion / print styles.
- `assets/home.css`: homepage-only forest, lime, and lavender visual system.
- `assets/site.js`: progressive enhancement for mobile navigation, keyboard
  recognition tabs, the image viewer, a before/after comparison, annotation
  previews, and bounded draggable pins with keyboard support. No-JavaScript
  pages keep all recognition panels and a static comparison visible.
- `scripts/build-site.py`: static page rendering, JSON-LD, sitemap, and URL paths.

Validate claims against the application and user guide when updating content.
The primary download button points to the latest release page instead of a
hard-coded release filename, so it continues to work after a new release.

## Screenshot assets and reproducibility

`assets/images` contains responsive WebP derivatives (640 / 1280 px), full-size
lossless WebP images, and two 1200 × 630 PNG social cards. A full-size image opens
when a screenshot is selected. All imagery uses existing project material;
model output has not been replaced or improved by hand. See
[`Screenshot/Blog/README.md`](../Screenshot/Blog/README.md) for provenance.

The cutout slider uses aligned crops from the existing actual before/after
comparison; the original image proportions and inference output are retained.
The Qwen panel uses an existing local translation result. Hero paper layouts,
annotation sample dashboard, abstract pin artwork, and `privacy-orbit.svg` are
original code-based illustrations, labeled as such. They do not run AI in the
browser or imply desktop performance measurements. No assets, copy, or code
from third-party software sites are included.

Some application renders were produced with 1.0.4; they illustrate features, not
an assertion that the current release looks identical. Recognition figures show
real CPU outputs; the desktop-pin composition is an illustrative scene. These
distinctions are also disclosed in the public pages.

To regenerate committed image derivatives on Windows with Pillow installed:

```powershell
python scripts/prepare-site-images.py
```

The script uses Microsoft YaHei / Segoe UI for social-card text. Image generation
is an editorial step and is **not** required by CI. Regenerate assets after
changing source screenshots, inspect them, then rebuild the website.

## Search discovery

The build provides canonical URLs, language alternates, Open Graph / Twitter
cards, SoftwareApplication / FAQPage / TechArticle JSON-LD, and `sitemap.xml`.
Submit **https://jietu.nimcap.com/sitemap.xml** in the relevant
search webmaster consoles after verifying ownership. Structured data and sitemap
submission do not guarantee indexing or recommendations.

On the custom domain, `/robots.txt` serves the actual host-root crawler policy.
For a project-path preview, `/nimcap-screenshot/robots.txt` would not control the host
root's policy. Keep public content crawlable and maintain accurate, useful
guides rather than adding hidden text.

### Notify IndexNow

`indexnow-key.txt` contains a deliberately public ownership key, not a password
or account API credential. The build publishes it as `/<key>.txt` under the
site's base path. This supports GitHub project Pages: `keyLocation` restricts
submission to the site's configured URL prefix. On the custom domain this is
the domain root; on a project-path preview it is limited to that subdirectory.

After the ownership file is publicly deployed, notify IndexNow manually:

```powershell
python scripts/submit-indexnow.py --dry-run
python scripts/submit-indexnow.py --report .artifacts/site-qa/indexnow-receipt.json
```

The command verifies the live key, reads the public sitemap, and submits its
URLs to the official IndexNow endpoint. Participating engines, including Bing,
share notifications. HTTP 200 means received; HTTP 202 means received with key
validation pending. Neither means indexed. Notifications are manual to avoid
resubmitting every page on every build. Protocol: https://www.indexnow.org/documentation.

Google does not participate in IndexNow. To submit there, sign in to Google
Search Console, add the URL-prefix property
`https://jietu.nimcap.com/`, verify ownership, then submit
`https://jietu.nimcap.com/sitemap.xml` in **Sitemaps**. Account
verification requires the property owner's session and cannot be inferred from
GitHub repository access. The former unauthenticated Google sitemap ping
endpoint is deprecated and is not used here.
