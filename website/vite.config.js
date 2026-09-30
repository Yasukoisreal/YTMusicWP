import { defineConfig } from 'vite'

// Static page: index.html holds all the content, src/main.js only adds effects
export default defineConfig({
  base: './',
  build: {
    // Keep the CSS as written: minifiers may drop the plain-colour fallbacks old Lumia browsers rely on
    cssMinify: false,
  },
})
