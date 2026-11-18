# SnapSharp website

The public site targets **https://leelongjiang.github.io/SnapSharp/**. It contains
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

For a local preview at the same repository subpath as production, use:

```powershell
python scripts/build-site.py --base-url http://localhost:8080/SnapSharp --output .artifacts/site-preview/SnapSharp
python -m http.server 8080 --directory .artifacts/site-preview
```

Then open **http://localhost:8080/SnapSharp/**. The output directory is ignored by
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
settings and keep DNS / HTTPS settings consistent; no CNAME is assumed here.

## Edit content and appearance

- `content.py`: Chinese/English editorial pairs, feature descriptions, FAQs, and
  guides. Chinese is first and English is second in each pair.
- `templates/base.html`: semantic page shell and metadata.
- `assets/site.css`: responsive layout and reduced-motion / print styles.
- `assets/site.js`: progressive enhancement for mobile navigation, keyboard
  screenshot tabs, and the image viewer. No-JavaScript pages keep the gallery.
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
Submit **https://leelongjiang.github.io/SnapSharp/sitemap.xml** in the relevant
search webmaster consoles after verifying ownership. Structured data and sitemap
submission do not guarantee indexing or recommendations.

On a project Pages site, `/SnapSharp/robots.txt` is **not** the host-root
`/robots.txt` that governs crawler access. The generated file documents intent;
it cannot override the host's crawler policy. With a custom domain at its root,
the same file can serve as the actual robots policy. Keep public content
crawlable and maintain accurate, useful guides rather than adding hidden text.
