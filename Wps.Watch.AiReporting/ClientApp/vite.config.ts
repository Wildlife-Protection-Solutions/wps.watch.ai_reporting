import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The front end builds into the ASP.NET app's wwwroot/ so it's served same-origin
// with the REST API (no CORS). In dev (`npm run dev`) Vite serves on :5173 and
// proxies /api to the running .NET app. The .NET app forces HTTPS, so the proxy
// targets the https profile and accepts its self-signed dev cert (secure: false).
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../wwwroot",
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: "https://localhost:7202",
        changeOrigin: true,
        secure: false,
      },
    },
  },
});
