# YTMusicWP website

The project page at https://yasukoisreal.github.io/YTMusicWP/, deployed by `.github/workflows/deploy-pages.yml`.

- `index.html` holds all the content, so the page reads fine in browsers without ES modules (IE Mobile 11 on Windows Phone 8.1, EdgeHTML on Windows 10 Mobile).
- `src/main.js` adds the effects (3D hero, spinning disc, Live Tile flips, screen reel, scroll reveal) where modules run, and respects `prefers-reduced-motion`.
- `public/shots/` has the screenshots as 360 px and 720 px JPEGs (resized from the full-size screenshots; the README uses the 720 px set in the repo's `Pictures/screenshots/`).

```bash
npm install
npm run dev
npm run build
```
