// src/Web/src/components/AuthButton.tsx
import { msalInstance } from "../lib/msal";
import { API_SCOPES } from "../lib/msalScopes";

export default function AuthButton() {
  const login = async () => {
    const accounts = msalInstance.getAllAccounts();
    if (accounts.length === 0) {
      await msalInstance.loginPopup({
        // 동의 한 번에 받으려면 API_SCOPE 포함 (선호)
        scopes: ["openid", "profile", ...API_SCOPES]
      });
    }
  };

  const logout = async () => {
    await msalInstance.logoutPopup();
  };

  const isAuthed = msalInstance.getAllAccounts().length > 0;

  return (
    <button onClick={isAuthed ? logout : login} className="border px-3 py-1 rounded">
      {isAuthed ? "Logout" : "Login"}
    </button>
  );
}