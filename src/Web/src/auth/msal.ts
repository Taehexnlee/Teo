import { type Configuration, LogLevel, PublicClientApplication } from "@azure/msal-browser";

const clientId = import.meta.env.VITE_AUTH_CLIENT_ID as string;
const authority = import.meta.env.VITE_AUTH_AUTHORITY as string;
const knownAuthority = import.meta.env.VITE_AUTH_KNOWN_AUTH as string;
const redirectUri = import.meta.env.VITE_AUTH_REDIRECT_URI as string;

export const msalConfig: Configuration = {
  auth: {
    clientId,
    authority,               // ex) https://<tenant>.ciamlogin.com/<tenant>.onmicrosoft.com/B2C_1_susi/v2.0/
    knownAuthorities: [knownAuthority], // ex) https://<tenant>.ciamlogin.com/<tenant>.onmicrosoft.com/v2.0/
    redirectUri,
    navigateToLoginRequestUrl: true,
  },
  cache: {
    cacheLocation: "localStorage",
    storeAuthStateInCookie: false,
  },
  system: {
    loggerOptions: {
      loggerCallback(level, message) {
        if (level === LogLevel.Error) console.error(message);
      },
      logLevel: LogLevel.Error,
    },
  },
};

export const pca = new PublicClientApplication(msalConfig);
