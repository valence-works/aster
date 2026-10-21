import type { FacetDto } from "@/lib/api";

// Mirrors Aster.Core.Models.Definitions.UiHints; docs/ui-hints.md describes each hint.
export const UiHints = {
  text: "text",
  multiline: "multiline",
  markdown: "markdown",
  html: "html",
  slug: "slug",
  email: "email",
  url: "url",
  number: "number",
  select: "select",
  radio: "radio",
  checkbox: "checkbox",
  switch: "switch",
  date: "date",
  datetime: "datetime",
  tags: "tags",
  referencePicker: "reference-picker",
  json: "json",
  code: "code",
  secret: "secret",
} as const;

export const standardUiHints: string[] = Object.values(UiHints);

// Names Studio used before the standard vocabulary (#107); aspects seeded earlier still carry them.
const legacyUiHints: Record<string, string> = { toggle: UiHints.switch, richtext: UiHints.markdown };

export function uiHintOf(facet: FacetDto): string | undefined {
  const hint = facet.metadata?.uiHint;
  return hint ? legacyUiHints[hint] ?? hint : undefined;
}
