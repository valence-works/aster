import { expect, test } from "@playwright/test";
import { unique } from "./support";

// #175: the Aspect Library creates and saves through the package's conditional registration.
test("creates and saves a shared aspect, and refuses a save based on a stale version", async ({ page }) => {
  const aspectId = unique("pricing");
  const register = (expectedLatestVersion?: number) => page.request.post(
    `/api/aster/aspects${expectedLatestVersion === undefined ? "" : `?expectedLatestVersion=${expectedLatestVersion}`}`,
    {
      headers: { "X-Aster-Tenant": "default" },
      data: {
        aspectDefinitionId: aspectId,
        id: crypto.randomUUID(),
        version: 0,
        requiresName: false,
        facetDefinitions: [{ facetDefinitionId: "amount", id: crypto.randomUUID(), version: 1, type: "decimal", isRequired: false }],
      },
    });

  await page.goto("/aspects/new");
  await page.locator("#aspectId").fill(aspectId);
  await page.getByPlaceholder("amount").fill("amount");
  await page.getByRole("button", { name: "Create aspect" }).click();
  await expect(page.getByText("Stored as version 1.")).toBeVisible();
  await expect(page).toHaveURL(new RegExp(`/aspects/${aspectId}$`));

  const description = page.getByPlaceholder("Shown as help text in the generated form");
  await description.fill("Price in cents");
  await page.getByRole("button", { name: "Save as new version" }).click();
  await expect(page.getByText("saved as version 2")).toBeVisible();

  // Another writer registers version 3 while the form still holds version 2.
  expect((await register(2)).status()).toBe(201);
  await description.fill("Price in euros");
  await page.getByRole("button", { name: "Save as new version" }).click();
  await expect(page.getByText(`Aspect '${aspectId}' was changed since you opened it. Reload and reapply your changes.`)).toBeVisible();

  const stale = await register(2);
  expect(stale.status()).toBe(409);
  expect((await stale.json()).code).toBe("aspect-version-conflict");
  expect((await register(0)).status()).toBe(409);
  expect((await register()).status()).toBe(400);
  expect((await page.request.post("/api/studio/aspects", { data: {} })).ok()).toBe(false);
});
