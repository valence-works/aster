import type { DefinitionDetailDto, DiagnosticDto, FacetDto, ResourceAspects } from "@/lib/api";
import { toKebabCase } from "@/lib/utils";

export type FieldErrors = Record<string, string>;

export function fieldKey(aspectKey: string, facetId: string) {
  return `${aspectKey}.${facetId}`;
}

export function initialAspects(definition: DefinitionDetailDto, resource?: { aspects: ResourceAspects } | null): ResourceAspects {
  const aspects: ResourceAspects = {};
  for (const aspect of definition.aspects) {
    aspects[aspect.aspectKey] = {};
    for (const facet of aspect.facets) {
      const existing = resource?.aspects?.[aspect.aspectKey]?.[facet.facetId];
      aspects[aspect.aspectKey][facet.facetId] = existing ?? defaultValue(facet);
    }
  }
  return aspects;
}

export function defaultValue(facet: FacetDto): unknown {
  if (facet.metadata?.defaultValue !== undefined) return facet.metadata.defaultValue;
  if (facet.metadata?.uiHint === "toggle" || facet.type === "boolean") return false;
  if (facet.metadata?.uiHint === "tags") return "";
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
      if (isEmpty(value)) continue;
      if (metadata?.minLength !== undefined && String(value).length < metadata.minLength) errors[key] = `Must be at least ${metadata.minLength} characters.`;
      if (metadata?.maxLength !== undefined && String(value).length > metadata.maxLength) errors[key] = `Must be at most ${metadata.maxLength} characters.`;
      const numeric = typeof value === "number" ? value : Number(value);
      if ((facet.type === "number" || metadata?.uiHint === "number") && !Number.isNaN(numeric)) {
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
      if (value === "") {
        normalized[aspect.aspectKey][facet.facetId] = null;
      } else if (facet.type === "number" || facet.metadata?.uiHint === "number") {
        normalized[aspect.aspectKey][facet.facetId] = typeof value === "number" ? value : Number(value);
      } else if (facet.type === "boolean" || facet.metadata?.uiHint === "toggle") {
        normalized[aspect.aspectKey][facet.facetId] = Boolean(value);
      } else if (facet.metadata?.uiHint === "slug") {
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
