import { useEffect, useState } from "react";
import { ArrowLeftRight, BookTemplate, Boxes, Building2, ChevronRight, DatabaseZap, Layers3, LayoutDashboard, LibraryBig, ListFilter, MoreHorizontal, PanelLeftClose, Sparkles } from "lucide-react";
import { NavLink, Outlet, useLocation, useNavigate, useParams } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { api, currentTenant, selectTenant } from "@/lib/api";
import { toTitle } from "@/lib/utils";
import { useAsync } from "@/hooks/useAsync";

const staticNav = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard },
  { to: "/content-types", label: "Content Types", icon: BookTemplate },
  { to: "/aspects", label: "Aspect Library", icon: LibraryBig },
  { to: "/portability", label: "Portability", icon: ArrowLeftRight },
  { to: "/query", label: "Query Builder", icon: ListFilter },
];

// Screens about one definition, resource or aspect; their entity may not exist in another tenant.
const entityRoute = /^\/(content|content-types|aspects)\/./;

export function AppShell() {
  const [tenant, setTenant] = useState(currentTenant);
  const tenants = useAsync(api.getTenants, []);
  const definitions = useAsync(api.getDefinitions, [tenant]);
  const location = useLocation();
  const navigate = useNavigate();
  const params = useParams();
  const crumbs = makeCrumbs(location.pathname, params.definitionId);
  const tenantName = tenants.data?.find((candidate) => candidate.tenantId === tenant)?.displayName ?? tenant;

  function switchTenant(next: string) {
    selectTenant(next);
    setTenant(next);
    if (entityRoute.test(location.pathname)) navigate("/");
  }

  // A stored tenant the host no longer knows would fail every request.
  useEffect(() => {
    if (tenants.data && !tenants.data.some((candidate) => candidate.tenantId === tenant)) switchTenant("default");
  }, [tenants.data]);

  return (
    <TooltipProvider delayDuration={250}>
      <div className="min-h-screen lg:grid lg:grid-cols-[288px_1fr]">
        <aside className="hidden border-r border-border/60 bg-background/45 px-4 py-5 lg:block">
          <div className="flex h-full flex-col gap-6">
            <div className="flex items-center gap-3 px-2">
              <div className="relative flex h-10 w-10 items-center justify-center rounded-xl border border-primary/30 bg-primary/10 text-primary shadow-seam">
                <Sparkles className="h-5 w-5" />
                <span className="absolute -right-1 -top-1 h-2.5 w-2.5 rounded-full bg-studio-live" />
              </div>
              <div>
                <div className="text-base font-semibold tracking-[-0.04em]">Aster Studio</div>
                <div className="text-xs text-muted-foreground">Horizontal CMS demo</div>
              </div>
            </div>
            <nav className="space-y-1">
              {staticNav.map((item) => <SidebarLink key={item.to} {...item} />)}
            </nav>
            <div className="space-y-3">
              <div className="flex items-center gap-2 px-2 text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground"><Layers3 className="h-3.5 w-3.5" /> Content</div>
              <nav className="space-y-1">
                {definitions.loading ? Array.from({ length: 3 }).map((_, index) => <Skeleton key={index} className="h-9 rounded-md" />) : null}
                {definitions.data?.map((definition) => <SidebarLink key={definition.definitionId} to={`/content/${definition.definitionId}`} label={definition.displayName} icon={Boxes} badge={definition.resourceCount} />)}
              </nav>
            </div>
            <div className="mt-auto rounded-lg border border-border/70 bg-card/70 p-4 facet-seam">
              <div className="flex items-center gap-2 text-sm font-medium"><DatabaseZap className="h-4 w-4 text-primary" /> Aspect reuse</div>
              <p className="mt-2 text-xs leading-5 text-muted-foreground">Attach SEO once, then watch the same facets, validation, and UI appear across articles, pages, and whatever you model next.</p>
            </div>
          </div>
        </aside>
        <main className="min-w-0">
          <header className="sticky top-0 z-30 flex h-16 items-center justify-between border-b border-border/60 bg-background/78 px-4 backdrop-blur-xl lg:px-8">
            <div className="flex min-w-0 items-center gap-3">
              <Button variant="ghost" size="icon" className="lg:hidden"><PanelLeftClose className="h-5 w-5" /></Button>
              <div className="min-w-0">
                <div className="flex items-center gap-1 text-xs text-muted-foreground">
                  {crumbs.map((crumb, index) => <span key={`${crumb}-${index}`} className="flex items-center gap-1">{index > 0 ? <ChevronRight className="h-3 w-3" /> : null}{crumb}</span>)}
                </div>
                <h1 className="truncate text-xl font-semibold tracking-[-0.04em]">{crumbs[crumbs.length - 1]}</h1>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <Select value={tenant} onValueChange={switchTenant}>
                <SelectTrigger aria-label="Tenant" className="h-9 w-36 gap-2"><Building2 className="h-4 w-4 shrink-0 text-muted-foreground" /><SelectValue /></SelectTrigger>
                <SelectContent>{(tenants.data ?? [{ tenantId: tenant, displayName: tenant }]).map((candidate) => <SelectItem key={candidate.tenantId} value={candidate.tenantId}>{candidate.displayName}</SelectItem>)}</SelectContent>
              </Select>
              <Tooltip>
                <TooltipTrigger asChild><Badge variant="schema" className="hidden sm:inline-flex">API contract v1</Badge></TooltipTrigger>
                <TooltipContent>Frontend calls only the frozen /api contract.</TooltipContent>
              </Tooltip>
              <DropdownMenu>
                <DropdownMenuTrigger asChild><Button variant="outline" size="icon"><MoreHorizontal className="h-4 w-4" /><span className="sr-only">Open studio menu</span></Button></DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuLabel>Studio shortcuts</DropdownMenuLabel>
                  <DropdownMenuItem asChild><NavLink to="/aspects">Aspect Library</NavLink></DropdownMenuItem>
                  <DropdownMenuItem asChild><NavLink to="/content-types">Content Types</NavLink></DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem disabled>Backend: http://localhost:5210</DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          </header>
          {/* Keyed by tenant, so every screen reloads its data from the newly selected tenant. */}
          <div key={tenant} className="mx-auto w-full max-w-7xl px-4 py-6 lg:px-8 lg:py-8"><Outlet context={{ reloadDefinitions: definitions.reload, tenantName }} /></div>
        </main>
      </div>
    </TooltipProvider>
  );
}

function SidebarLink({ to, label, icon: Icon, badge }: { to: string; label: string; icon: React.ComponentType<{ className?: string }>; badge?: number }) {
  return (
    <NavLink to={to} end={to === "/"} className={({ isActive }) => `group flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors ${isActive ? "bg-primary/10 text-primary shadow-seam" : "text-muted-foreground hover:bg-accent/60 hover:text-foreground"}`}>
      <Icon className="h-4 w-4" />
      <span className="flex-1 truncate">{label}</span>
      {badge !== undefined ? <span className="rounded-full bg-secondary px-2 py-0.5 text-[11px] text-muted-foreground group-[.active]:text-primary">{badge}</span> : null}
    </NavLink>
  );
}

function makeCrumbs(pathname: string, definitionId?: string) {
  if (pathname === "/") return ["Dashboard"];
  if (pathname.startsWith("/aspects")) return ["Aster Studio", "Aspect Library"];
  if (pathname.startsWith("/content-types/")) return ["Aster Studio", "Content Types", toTitle(pathname.split("/").pop() ?? "Detail")];
  if (pathname.startsWith("/content-types")) return ["Aster Studio", "Content Types"];
  if (pathname.startsWith("/portability")) return ["Aster Studio", "Portability"];
  if (pathname.startsWith("/query")) return ["Aster Studio", "Query Builder"];
  if (pathname.includes("/new")) return ["Aster Studio", "Content", toTitle(definitionId ?? "Content"), "New Resource"];
  if (pathname.startsWith("/content/")) return ["Aster Studio", "Content", toTitle(definitionId ?? "Content")];
  return ["Aster Studio"];
}
