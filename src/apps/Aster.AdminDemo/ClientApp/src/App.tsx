import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "@/shell/AppShell";
import { Dashboard } from "@/pages/Dashboard";
import { AspectLibrary } from "@/pages/AspectLibrary";
import { ContentTypes } from "@/pages/ContentTypes";
import { DefinitionDetail } from "@/pages/DefinitionDetail";
import { ContentList } from "@/pages/ContentList";
import { ResourceEditor } from "@/pages/ResourceEditor";

export default function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<Dashboard />} />
        <Route path="aspects" element={<AspectLibrary />} />
        <Route path="content-types" element={<ContentTypes />} />
        <Route path="content-types/:definitionId" element={<DefinitionDetail />} />
        <Route path="content/:definitionId" element={<ContentList />} />
        <Route path="content/:definitionId/new" element={<ResourceEditor mode="create" />} />
        <Route path="content/:definitionId/:resourceId" element={<ResourceEditor mode="edit" />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
