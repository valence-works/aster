import { Badge } from "@/components/ui/badge";
import type { FacetDto, FacetMetadata } from "@/lib/api";

export function ConstraintBadges({ facet }: { facet: FacetDto }) {
  const metadata = facet.metadata;
  const badges = buildConstraintBadges(facet.isRequired, metadata);
  if (badges.length === 0) return <span className="text-muted-foreground">No constraints</span>;
  return <div className="flex flex-wrap gap-1.5">{badges.map((badge) => <Badge key={badge} variant="secondary">{badge}</Badge>)}</div>;
}

export function buildConstraintBadges(isRequired: boolean, metadata: FacetMetadata | null) {
  const badges: string[] = [];
  if (isRequired) badges.push("required");
  if (!metadata) return badges;
  if (metadata.uiHint) badges.push(`ui:${metadata.uiHint}`);
  if (metadata.minLength !== undefined) badges.push(`min ${metadata.minLength}`);
  if (metadata.maxLength !== undefined) badges.push(`max ${metadata.maxLength}`);
  if (metadata.minValue !== undefined) badges.push(`≥ ${metadata.minValue}`);
  if (metadata.maxValue !== undefined) badges.push(`≤ ${metadata.maxValue}`);
  if (metadata.pattern) badges.push("pattern");
  if (metadata.allowedValues?.length) badges.push(`${metadata.allowedValues.length} values`);
  if (metadata.minItems !== undefined) badges.push(`≥ ${metadata.minItems} items`);
  if (metadata.maxItems !== undefined) badges.push(`≤ ${metadata.maxItems} items`);
  if (metadata.uniqueItems) badges.push("unique");
  return badges;
}
