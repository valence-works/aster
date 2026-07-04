import { X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";

export function TagInput({ value, onChange, disabled }: { value: string; onChange: (value: string) => void; disabled?: boolean }) {
  const tags = value.split(",").map((tag) => tag.trim()).filter(Boolean);
  function remove(tag: string) {
    onChange(tags.filter((item) => item !== tag).join(", "));
  }
  return (
    <div className="space-y-2">
      <Input value={value} onChange={(event) => onChange(event.target.value)} placeholder="docs, launch, featured" disabled={disabled} />
      <div className="flex min-h-6 flex-wrap gap-1.5">
        {tags.map((tag) => <Badge key={tag} variant="facet" className="gap-1">{tag}{!disabled ? <button type="button" onClick={() => remove(tag)} className="rounded-full hover:text-foreground"><X className="h-3 w-3" /></button> : null}</Badge>)}
      </div>
    </div>
  );
}
