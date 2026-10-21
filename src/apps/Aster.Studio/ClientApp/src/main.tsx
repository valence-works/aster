import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { Toaster } from "sonner";
import App from "./App";
import "./index.css";

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <BrowserRouter>
      <App />
      <Toaster theme="dark" richColors closeButton position="bottom-right" toastOptions={{ className: "border border-border bg-popover text-popover-foreground" }} />
    </BrowserRouter>
  </React.StrictMode>,
);
