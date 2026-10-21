import type { DefinitionDetailDto, DiagnosticDto, FacetDto, ResourceAspects } from "@/lib/api";
import { UiHints, uiHintOf } from "@/lib/uiHints";
import { toKebabCase } from "@/lib/utils";

export type FieldErrors = Record<string, string>;

/** Array facets use the ADR 0002 descriptor form, such as `string[]`. */
export function isArrayFacet(facet: FacetDto) {
  return facet.type.endsWith("[]");
}

/** Reads an array facet value; legacy comma-separated strings are split into elements. */
export function toStringArray(value: unknown): string[] {
  if (Array.isArray(value)) return value.map((item) => String(item));
  if (typeof value === "string") return value.split(",").map((item) => item.trim()).filter(Boolean);
  return [];
}

export function fieldKey(aspectKey: string, facetId: string) {
  return `${aspectKey}.${facetId}`;
}

export function initialAspects(definition: DefinitionDetailDto, resource?: { aspects: ResourceAspects } | null): ResourceAspects {
  const aspects: ResourceAspects = {};
  for (const aspect of definition.aspects) {
    aspects[aspect.aspectKey] = {};
    for (const facet of aspect.facets) {
      const existing = resource?.aspects?.[aspect.aspectKey]?.[facet.facetId];
      const value = existing ?? defaultValue(facet);
      aspects[aspect.aspectKey][facet.facetId] = isArrayFacet(facet) ? toStringArray(value) : value;
    }
  }
  return aspects;
}

export function defaultValue(facet: FacetDto): unknown {
  if (facet.metadata?.defaultValue !== undefined) return facet.metadata.defaultValue;
  if (uiHintOf(facet) === UiHints.switch || uiHintOf(facet) === UiHints.checkbox || facet.type === "boolean") return false;
  if (uiHintOf(facet) === UiHints.tags || isArrayFacet(facet)) return [];
  return "";
}

export function validateAspects(definition: DefinitionDetailDto, aspects: ResourceAspects): FieldErrors {
  const errors: FieldErrors = {};
  for (const aspect of definition.aspects) {
    for (const facet of aspect.facets) {
      const value = aspects[aspect.aspectKey]?.[facet.facetId];
      const metadata = facet.metadata;
      const key = fieldKey(aspect.aspectKey, facet.facetId);
      if (facet.isRequired && isEmpty(value)) {
        errors[key] = `${metadata?.label ?? facet.facetId} is required.`;
        continue;
      }
      if (isEmpty(value) && !(isArrayFacet(facet) && metadata?.minItems)) continue;
      if (isArrayFacet(facet)) {
        const error = validateArray(toStringArray(value), metadata);
        if (error) errors[key] = error;
        continue;
      }
      if (metadata?.minLength !== undefined && String(value).length < metadata.minLength) errors[key] = `Must be at least ${metadata.minLength} characters.`;
      if (metadata?.maxLength !== undefined && String(value).length > metadata.maxLength) errors[key] = `Must be at most ${metadata.maxLength} characters.`;
      const numeric = typeof value === "number" ? value : Number(value);
      if ((facet.type === "number" || uiHintOf(facet) === UiHints.number) && !Number.isNaN(numeric)) {
        if (metadata?.minValue !== undefined && numeric < metadata.minValue) errors[key] = `Must be at least ${metadata.minValue}.`;
        if (metadata?.maxValue !== undefined && numeric > metadata.maxValue) errors[key] = `Must be at most ${metadata.maxValue}.`;
      }
      if (metadata?.pattern) {
        try {
          if (!new RegExp(metadata.pattern).test(String(value))) errors[key] = `Must match pattern ${metadata.pattern}.`;
        } catch {
          errors[key] = "Configured pattern is invalid.";
        }
      }
    }
  }
  return errors;
}

function validateArray(items: string[], metadata: FacetDto["metadata"]): string | undefined {
  if (metadata?.minItems !== undefined && items.length < metadata.minItems) return `Add at least ${metadata.minItems} item(s).`;
  if (metadata?.maxItems !== undefined && items.length > metadata.maxItems) return `Use at most ${metadata.maxItems} item(s).`;
  if (metadata?.uniqueItems && new Set(items).size !== items.length) return "Items must be unique.";
  for (const item of items) {
    if (metadata?.minLength !== undefined && item.length < metadata.minLength) return `"${item}" must be at least ${metadata.minLength} characters.`;
    if (metadata?.maxLength !== undefined && item.length > metadata.maxLength) return `"${item}" must be at most ${metadata.maxLength} characters.`;
    if (metadata?.pattern) {
      try {
        if (!new RegExp(metadata.pattern).test(item)) return `"${item}" must match pattern ${metadata.pattern}.`;
      } catch {
        return "Configured pattern is invalid.";
      }
    }
  }
  return undefined;
}

export function diagnosticsToFieldErrors(diagnostics: DiagnosticDto[]) {
  const fieldErrors: FieldErrors = {};
  const formErrors: string[] = [];
  for (const diagnostic of diagnostics) {
    if (diagnostic.aspectKey && diagnostic.facetId) fieldErrors[fieldKey(diagnostic.aspectKey, diagnostic.facetId)] = diagnostic.message;
    else formErrors.push(`${diagnostic.code}: ${diagnostic.message}`);
  }
  return { fieldErrors, formErrors };
}

function isEmpty(value: unknown) {
  return value === null || value === undefined || value === "" || (Array.isArray(value) && value.length === 0);
}

export function normalizeForSubmit(definition: DefinitionDetailDto, aspects: ResourceAspects): ResourceAspects {
  const normalized: ResourceAspects = {};
  for (const aspect of definition.aspects) {
    normalized[aspect.aspectKey] = {};
    for (const facet of aspect.facets) {
      const value = aspects[aspect.aspectKey]?.[facet.facetId];
      if (isArrayFacet(facet)) {
        const items = toStringArray(value);
        normalized[aspect.aspectKey][facet.facetId] = items.length ? items : null;
      } else if (value === "") {
        normalized[aspect.aspectKey][facet.facetId] = null;
      } else if (facet.type === "number" || uiHintOf(facet) === UiHints.number) {
        normalized[aspect.aspectKey][facet.facetId] = typeof value === "number" ? value : Number(value);
      } else if (facet.type === "boolean" || uiHintOf(facet) === UiHints.switch || uiHintOf(facet) === UiHints.checkbox) {
        normalized[aspect.aspectKey][facet.facetId] = Boolean(value);
      } else if (uiHintOf(facet) === UiHints.slug) {
        normalized[aspect.aspectKey][facet.facetId] = toKebabCase(String(value));
      } else {
        normalized[aspect.aspectKey][facet.facetId] = value;
      }
    }
  }
  return normalized;
}

export function inputDateValue(value: unknown, mode: "date" | "datetime") {
  if (typeof value !== "string" || value.length === 0) return "";
  if (mode === "date") return value.slice(0, 10);
  return value.includes("T") ? value.slice(0, 16) : value;
}
