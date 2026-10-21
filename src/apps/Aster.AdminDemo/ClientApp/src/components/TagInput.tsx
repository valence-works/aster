import { useState } from "react";
import { X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";

/** Edits a string[] facet: Enter or comma adds a tag, Backspace on an empty input removes the last one. */
export function TagInput({ value, onChange, disabled, placeholder = "Add a tag and press Enter", id }: { value: string[]; onChange: (value: string[]) => void; disabled?: boolean; placeholder?: string; id?: string }) {
  const [draft, setDraft] = useState("");
  function add(raw: string) {
    const additions = raw.split(",").map((tag) => tag.trim()).filter(Boolean);
    const next = [...value];
    for (const tag of additions) {
      if (!next.some((existing) => existing.toLowerCase() === tag.toLowerCase())) next.push(tag);
    }
    if (next.length !== value.length) onChange(next);
    setDraft("");
  }
  function remove(tag: string) {
    onChange(value.filter((item) => item !== tag));
  }
  return (
    <div className="space-y-2">
      <Input
        id={id}
        value={draft}
        placeholder={placeholder}
        disabled={disabled}
        onChange={(event) => (event.target.value.includes(",") ? add(event.target.value) : setDraft(event.target.value))}
        onBlur={() => draft && add(draft)}
        onKeyDown={(event) => {
          if (event.key === "Enter") {
            event.preventDefault();
            add(draft);
          } else if (event.key === "Backspace" && draft === "" && value.length > 0) {
            onChange(value.slice(0, -1));
          }
        }}
      />
      <div className="flex min-h-6 flex-wrap gap-1.5">
        {value.map((tag) => <Badge key={tag} variant="facet" className="gap-1">{tag}{!disabled ? <button type="button" aria-label={`Remove ${tag}`} onClick={() => remove(tag)} className="rounded-full hover:text-foreground"><X className="h-3 w-3" /></button> : null}</Badge>)}
      </div>
    </div>
  );
}
