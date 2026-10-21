import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig, devices } from "@playwright/test";

// The smoke suite drives the real host: the ASP.NET Core app serving the built SPA (`npm run build` first) and
// the Aster API, seeded into a fresh SQLite database so every run starts from the same demo content.
const port = Number(process.env.STUDIO_E2E_PORT ?? 5299);
const baseURL = `http://localhost:${port}`;
const database = path.join(mkdtempSync(path.join(tmpdir(), "aster-studio-e2e-")), "studio.db");
const studioProject = fileURLToPath(new URL("..", import.meta.url));

export default defineConfig({
  testDir: "./e2e",
  timeout: 30_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: `dotnet run --project "${studioProject}" --no-launch-profile`,
    url: `${baseURL}/api/studio/definitions`,
    timeout: 180_000,
    reuseExistingServer: false,
    stdout: "pipe",
    env: {
      ASPNETCORE_ENVIRONMENT: "Development",
      Urls: baseURL,
      Aster__ConnectionString: `Data Source=${database}`,
    },
  },
});
