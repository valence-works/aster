import { Wand2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { ConstraintBadges } from "@/components/FacetBadges";
import { TagInput } from "@/components/TagInput";
import type { DefinitionDetailDto, FacetDto, ResourceAspects } from "@/lib/api";
import { fieldKey, inputDateValue, type FieldErrors } from "@/lib/resourceForm";
import { toKebabCase } from "@/lib/utils";

export function DynamicResourceForm({ definition, aspects, errors, onChange, readOnly = false }: { definition: DefinitionDetailDto; aspects: ResourceAspects; errors: FieldErrors; onChange?: (aspects: ResourceAspects) => void; readOnly?: boolean }) {
  function update(aspectKey: string, facetId: string, value: unknown) {
    if (!onChange) return;
    onChange({ ...aspects, [aspectKey]: { ...(aspects[aspectKey] ?? {}), [facetId]: value } });
  }

  function titleValue() {
    return String(aspects.content?.title ?? "");
  }

  return (
    <div className="space-y-5">
      {definition.aspects.map((aspect) => (
        <Card key={aspect.aspectKey} className="overflow-hidden">
          <CardHeader className="border-b border-border/60 bg-secondary/20">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div><CardTitle>{aspect.displayName}</CardTitle><CardDescription>Aspect key <span className="font-mono">{aspect.aspectKey}</span> · reusable horizontal facet group</CardDescription></div>
              <Badge variant="facet">plugs into any definition</Badge>
            </div>
          </CardHeader>
          <CardContent className="grid gap-5 pt-6 md:grid-cols-2">
            {[...aspect.facets].sort((a, b) => (a.metadata?.displayOrder ?? 999) - (b.metadata?.displayOrder ?? 999) || a.facetId.localeCompare(b.facetId)).map((facet) => {
              const value = aspects[aspect.aspectKey]?.[facet.facetId];
              const key = fieldKey(aspect.aspectKey, facet.facetId);
              const isWide = ["multiline", "richtext", "tags"].includes(facet.metadata?.uiHint ?? "");
              return (
                <div key={facet.facetId} className={isWide ? "space-y-2 md:col-span-2" : "space-y-2"}>
                  <div className="flex flex-wrap items-center justify-between gap-2"><Label htmlFor={key}>{facet.metadata?.label ?? facet.facetId}{facet.isRequired ? <span className="ml-1 text-destructive">*</span> : null}</Label><ConstraintBadges facet={facet} /></div>
                  {facet.metadata?.description ? <p className="text-xs leading-5 text-muted-foreground">{facet.metadata.description}</p> : null}
                  <FieldControl facet={facet} id={key} value={value} disabled={readOnly} onValue={(next) => update(aspect.aspectKey, facet.facetId, next)} onGenerateSlug={() => update(aspect.aspectKey, facet.facetId, toKebabCase(titleValue()))} />
                  {facet.metadata?.uiHint === "slug" ? <p className="text-xs text-muted-foreground">Live slug hint: <span className="font-mono text-primary">{toKebabCase(String(value || titleValue())) || "kebab-case-slug"}</span></p> : null}
                  {errors[key] ? <p className="text-sm text-destructive">{errors[key]}</p> : null}
                </div>
              );
            })}
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

function FieldControl({ facet, id, value, disabled, onValue, onGenerateSlug }: { facet: FacetDto; id: string; value: unknown; disabled: boolean; onValue: (value: unknown) => void; onGenerateSlug: () => void }) {
  const hint = facet.metadata?.uiHint;
  const stringValue = value === null || value === undefined ? "" : String(value);
  if (facet.metadata?.allowedValues?.length || hint === "select") {
    return <Select value={stringValue} onValueChange={onValue} disabled={disabled}><SelectTrigger id={id}><SelectValue placeholder="Select value" /></SelectTrigger><SelectContent>{(facet.metadata?.allowedValues ?? []).map((option) => <SelectItem key={String(option)} value={String(option)}>{String(option)}</SelectItem>)}</SelectContent></Select>;
  }
  if (hint === "toggle" || facet.type === "boolean") return <div className="flex h-10 items-center"><Switch checked={Boolean(value)} onCheckedChange={onValue} disabled={disabled} /></div>;
  if (hint === "tags") return <TagInput value={stringValue} onChange={onValue} disabled={disabled} />;
  if (hint === "multiline" || hint === "richtext") return <Textarea id={id} value={stringValue} onChange={(event) => onValue(event.target.value)} disabled={disabled} className={hint === "richtext" ? "min-h-44 font-mono" : undefined} />;
  if (hint === "slug") return <div className="flex gap-2"><Input id={id} value={stringValue} onChange={(event) => onValue(event.target.value)} onBlur={(event) => onValue(toKebabCase(event.currentTarget.value))} disabled={disabled} /><Button type="button" variant="outline" disabled={disabled} onClick={onGenerateSlug}><Wand2 className="h-4 w-4" />Title</Button></div>;
  if (hint === "date" || hint === "datetime") return <Input id={id} type="date" value={inputDateValue(value, "date")} onChange={(event) => onValue(event.target.value)} disabled={disabled} />;
  if (hint === "email" || hint === "url") return <Input id={id} type={hint} value={stringValue} onChange={(event) => onValue(event.target.value)} disabled={disabled} />;
  if (hint === "number" || facet.type === "number") return <Input id={id} type="number" min={facet.metadata?.minValue} max={facet.metadata?.maxValue} value={stringValue} onChange={(event) => onValue(event.target.value)} disabled={disabled} />;
  return <Input id={id} value={stringValue} onChange={(event) => onValue(event.target.value)} disabled={disabled} />;
}
