import { Navigate, Route, Routes } from "react-router-dom";
import { useAuth } from "./context/AuthContext";
import RegisterPage from "./features/auth/RegisterPage";
import LoginPage from "./features/auth/LoginPage";
import LobbyPage from "./features/lobby/LobbyPage";
import TablePage from "./features/table/TablePage";

function RequireAuth({ children }: { children: React.ReactNode }) {
  const { session, loading } = useAuth();
  if (loading) return <div className="loading">Loading…</div>;
  if (!session) return <Navigate to="/auth/login" replace />;
  return <>{children}</>;
}

export default function App() {
  return (
    <Routes>
      <Route path="/auth/register" element={<RegisterPage />} />
      <Route path="/auth/login" element={<LoginPage />} />
      <Route
        path="/"
        element={
          <RequireAuth>
            <LobbyPage />
          </RequireAuth>
        }
      />
      <Route
        path="/table/:tableId"
        element={
          <RequireAuth>
            <TablePage />
          </RequireAuth>
        }
      />
    </Routes>
  );
}
