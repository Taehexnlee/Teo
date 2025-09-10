import { Routes, Route, Link } from "react-router-dom";
import Organizations from "./pages/Organizations";
import OrgDetail from "./pages/OrgDetail";
import AuthButton from "./components/AuthButton";

export default function App() {
  return (
    <div className="max-w-4xl mx-auto">
      <header className="p-4 border-b mb-4 flex items-center justify-between gap-3">
        <Link to="/" className="text-xl font-semibold">Teo Web</Link>
        <div className="flex items-center gap-4">
          <nav className="text-sm">
            <Link to="/" className="underline">Organizations</Link>
          </nav>
          <AuthButton />
        </div>
      </header>

      <main>
        <Routes>
          <Route path="/" element={<Organizations />} />
          <Route path="/orgs/:id" element={<OrgDetail />} />
        </Routes>
      </main>
    </div>
  );
}