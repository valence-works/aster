import { expect, test, type Page } from "@playwright/test";
import { createArticle, saveNewVersion, setChannel, unique, versionRow } from "./support";

// Smoke coverage for the operations screens (#111): lifecycle markers and restore, policy preview and application,
// version pruning, schema upgrades and historical activation, all through the SDK services against SQLite.
// Each test that changes data creates it, except the schema upgrade, which needs an article saved before the seed
// bumped the article definition; no other test touches that article.

const lifecycle = (page: Page) => page.getByRole("region", { name: "Lifecycle" });
const schema = (page: Page) => page.getByRole("region", { name: "Schema" });
const diagnostics = (page: Page) => page.getByRole("list", { name: "Diagnostics" });
const candidates = (page: Page) => page.getByRole("table", { name: "Policy candidates" });
const applicationResult = (page: Page) => page.getByRole("region", { name: "Application result" });

async function filterByLifecycle(page: Page, label: string) {
  await page.getByRole("combobox", { name: "Lifecycle filter" }).click();
  await page.getByRole("option", { name: label }).click();
}

async function runPolicyPreview(page: Page) {
  await page.goto("/content-types/article");
  await page.getByRole("button", { name: "Run preview" }).click();
  await expect(candidates(page)).toBeVisible();
}

test("archives a resource, filters by lifecycle state, and restores it after a stale preview", async ({ page }) => {
  const title = unique("Lifecycle article");
  const resourceId = await createArticle(page, title);

  await lifecycle(page).getByRole("button", { name: "Archive" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Reason").fill("Superseded");
  await dialog.getByRole("button", { name: "Archive" }).click();
  await expect(dialog).toBeHidden();
  await expect(lifecycle(page).getByText("archived", { exact: true })).toBeVisible();
  await expect(lifecycle(page).getByText(/· Superseded$/)).toBeVisible();

  await page.goto("/content/article");
  await filterByLifecycle(page, "Archived");
  await expect(page.getByRole("row").filter({ hasText: title })).toBeVisible();
  await filterByLifecycle(page, "Active");
  await expect(page.getByText(/^Showing \d+–\d+ of \d+$/)).toBeVisible();
  await expect(page.getByRole("row").filter({ hasText: title })).toHaveCount(0);

  // Another writer swaps the archive marker for a soft-delete after this page read it.
  await page.goto(`/content/article/${resourceId}`);
  await expect(lifecycle(page).getByText("archived", { exact: true })).toBeVisible();
  expect((await page.request.post(`/api/studio/resources/${resourceId}/restore`, { data: { expectedState: "archived" } })).ok()).toBe(true);
  expect((await page.request.post(`/api/studio/resources/${resourceId}/lifecycle`, { data: { state: "soft-deleted" } })).ok()).toBe(true);

  // The restore preview expects the archive marker the page showed, so it fails with a stable code instead.
  await lifecycle(page).getByRole("button", { name: "Restore" }).click();
  await expect(dialog.getByText("failed", { exact: true })).toBeVisible();
  await expect(diagnostics(page).getByText("lifecycle-restore-marker-mismatch")).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Restore" })).toBeDisabled();
  await page.keyboard.press("Escape");

  await page.reload();
  await expect(lifecycle(page).getByText("soft-deleted", { exact: true })).toBeVisible();
  await lifecycle(page).getByRole("button", { name: "Restore" }).click();
  await expect(dialog.getByText("restorable", { exact: true })).toBeVisible();
  await dialog.getByRole("button", { name: "Restore" }).click();
  await expect(dialog).toBeHidden();
  await expect(lifecycle(page).getByText("active", { exact: true })).toBeVisible();
  await expect(lifecycle(page).getByRole("button", { name: "Archive" })).toBeVisible();
});

test("previews policies and applies archive outcomes, rejecting a stale candidate", async ({ page }) => {
  const draft = await createArticle(page, unique("Policy draft"));
  const edited = await createArticle(page, unique("Policy edited"));

  await runPolicyPreview(page);
  const declarations = page.getByRole("table", { name: "Policy declarations" });
  await expect(declarations.getByText("archive-drafts", { exact: true })).toBeVisible();
  await expect(declarations.getByText("keep-latest-version", { exact: true })).toBeVisible();
  await candidates(page).getByRole("checkbox", { name: `Select archive-drafts for ${draft} v1` }).check();
  await candidates(page).getByRole("checkbox", { name: `Select archive-drafts for ${edited} v1` }).check();

  // A new version lands after the preview, so that candidate no longer names the latest version.
  const update = await page.request.put(`/api/aster/resources/${edited}`, { data: { baseVersion: 1, aspects: { content: { title: "Edited after the preview" } } } });
  expect(update.ok()).toBe(true);

  await page.getByRole("button", { name: "Apply 2 archive/soft-delete" }).click();
  const result = applicationResult(page);
  await expect(result.getByRole("heading", { name: "Applied 1 of 2" })).toBeVisible();
  await expect(result.getByRole("group", { name: `${draft} v1` }).getByText("applied", { exact: true })).toBeVisible();
  const stale = result.getByRole("group", { name: `${edited} v1` });
  await expect(stale.getByText("failed", { exact: true })).toBeVisible();
  await expect(stale.getByText("policy-application-stale-candidate")).toBeVisible();

  // The preview reruns: the archived draft no longer matches, and the edited one is offered at its new version.
  await expect(candidates(page).getByRole("checkbox", { name: `Select archive-drafts for ${edited} v2` })).toBeVisible();
  await expect(candidates(page).getByRole("checkbox", { name: `Select archive-drafts for ${draft} v1` })).toHaveCount(0);

  await page.goto(`/content/article/${draft}`);
  await expect(lifecycle(page).getByText("archived", { exact: true })).toBeVisible();
});

test("prunes an unprotected version and reports active ones as protected", async ({ page }) => {
  const title = unique("Pruned article");
  const resourceId = await createArticle(page, title);
  await saveNewVersion(page, `${title} v2`, 2);
  await saveNewVersion(page, `${title} v3`, 3);
  await setChannel(page, 2, "Activate", "live");

  await runPolicyPreview(page);
  // The preview leaves the active version out and reports it, so pruning never meets the refusal (#187).
  await expect(candidates(page).getByRole("checkbox", { name: `Select keep-latest-version for ${resourceId} v2` })).toHaveCount(0);
  await expect(diagnostics(page).getByText("policy-pruning-version-protected-active").first()).toBeVisible();

  await candidates(page).getByRole("checkbox", { name: `Select keep-latest-version for ${resourceId} v1` }).check();
  await page.getByRole("button", { name: "Prune 1 versions" }).click();
  await expect(applicationResult(page).getByRole("heading", { name: "Pruned 1 of 1" })).toBeVisible();
  await expect(applicationResult(page).getByText("pruned", { exact: true })).toBeVisible();

  await page.goto(`/content/article/${resourceId}`);
  await expect(versionRow(page, 3).getByText("latest")).toBeVisible();
  await expect(versionRow(page, 2).getByText("protected")).toBeVisible();
  await expect(versionRow(page, 1)).toHaveCount(0);
});

test("upgrades an article saved against an older definition and reports carried-forward aspects", async ({ page }) => {
  // Seeded on article v1, which still attached navigation; the seed then registered v2 without it.
  await page.goto("/content/article/article-horizontal-aspects");
  await expect(schema(page).getByText("upgrade available")).toBeVisible();
  await expect(schema(page).getByText("v2 was saved against definition v1; the latest is v2.")).toBeVisible();
  await expect(schema(page).getByText("navigation")).toBeVisible();

  // The carried-forward keys come from the SDK's upgrade preview, which writes nothing.
  const preview = await (await page.request.get("/api/studio/resources/article-horizontal-aspects/schema")).json();
  expect(preview.upgrade).toEqual({ status: "upgraded", targetDefinitionVersion: 2, carriedForwardAspectKeys: ["navigation"], diagnostics: [] });

  await schema(page).getByRole("button", { name: "Upgrade to definition v2" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByText("navigation")).toBeVisible();
  await dialog.getByRole("button", { name: "Upgrade" }).click();
  await expect(dialog).toBeHidden();

  await expect(page.getByText("base v3")).toBeVisible();
  await expect(schema(page).getByText("current", { exact: true })).toBeVisible();
  await expect(schema(page).getByText("Last upgrade: upgraded, definition v1 → v2, carried forward navigation.")).toBeVisible();
  await expect(versionRow(page, 3).getByText("definition v2", { exact: false })).toBeVisible();
});

test("activates a historical version without moving latest, and shows maintenance hints", async ({ page }) => {
  const title = unique("Historical article");
  await createArticle(page, title);
  await saveNewVersion(page, `${title} v2`, 2);
  await expect(versionRow(page, 1).getByText("possible prune candidate")).toBeVisible();
  await expect(versionRow(page, 2).getByText("protected")).toBeVisible();

  await versionRow(page, 1).getByRole("button", { name: "Activate live" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByText("Historical activation: v2 stays the latest version.")).toBeVisible();
  await dialog.getByRole("button", { name: "Confirm" }).click();
  await expect(dialog).toBeHidden();

  await expect(versionRow(page, 1).getByRole("button", { name: "Deactivate live" })).toBeVisible();
  await expect(versionRow(page, 1).getByText("protected")).toBeVisible();
  await expect(versionRow(page, 2).getByText("latest")).toBeVisible();
  await expect(page.getByText("base v2")).toBeVisible();
});

test("answers operations on a missing resource or definition with a stable not-found code", async ({ page }) => {
  const codes = async (response: Awaited<ReturnType<typeof page.request.get>>, status: number) => {
    expect(response.status()).toBe(status);
    return ((await response.json()).diagnostics as { code: string }[]).map((diagnostic) => diagnostic.code);
  };

  expect(await codes(await page.request.get("/api/studio/resources/missing-resource/lifecycle"), 404)).toEqual(["resource-not-found"]);
  expect(await codes(await page.request.get("/api/studio/definitions/missing-definition/policies"), 404)).toEqual(["definition-not-found"]);
  expect(await codes(await page.request.post("/api/studio/resources/missing-resource/schema-upgrade", { data: { baseVersion: 1 } }), 404))
    .toEqual(["resource-not-found"]);

  // A stale base version on an existing resource is a conflict, not a missing version.
  const resourceId = await createArticle(page, unique("Upgrade base article"));
  expect(await codes(await page.request.post(`/api/studio/resources/${resourceId}/schema-upgrade`, { data: { baseVersion: 99 } }), 409))
    .toEqual(["concurrency-conflict"]);
});
