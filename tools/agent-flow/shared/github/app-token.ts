import { readFile } from 'node:fs/promises';
import { createAppAuth } from '@octokit/auth-app';

// The agent pushes and opens PRs as a GitHub App, not as the user. GitHub does not let you approve your own PR,
// so if the agent used the user's account, the user could not approve the agent's PRs.
// These environment variables say which App to use and where its private key is.
export const appVariableNames = {
  appId: 'AGENT_APP_ID',
  installationId: 'AGENT_APP_INSTALLATION_ID',
  privateKeyPath: 'AGENT_APP_PRIVATE_KEY_PATH',
} as const;

export type AppCredentials = { appId: number; installationId: number; privateKeyPath: string };

type Environment = Record<string, string | undefined>;

// Error messages name the variable but never show its value, because logs can be public.
export function readAppCredentials(environment: Environment): AppCredentials {
  const read = (name: string): string => {
    const value = environment[name]?.trim();
    if (!value) throw new Error(`the environment variable ${name} is not set`);
    return value;
  };
  const readId = (name: string): number => {
    const value = read(name);
    if (!/^[1-9][0-9]*$/.test(value)) throw new Error(`the environment variable ${name} is not a number`);
    return Number(value);
  };
  return {
    appId: readId(appVariableNames.appId),
    installationId: readId(appVariableNames.installationId),
    privateKeyPath: read(appVariableNames.privateKeyPath),
  };
}

// Gets a short-lived token for the App: it works for one hour, and only on repositories where the App is
// installed. We give it to one git or gh process at a time, through its environment. The Claude session never
// sees it.
export async function getInstallationToken(): Promise<string> {
  const credentials = readAppCredentials(process.env);
  let privateKey: string;
  try {
    privateKey = await readFile(credentials.privateKeyPath, 'utf8');
  } catch {
    throw new Error(`could not read the private key named by ${appVariableNames.privateKeyPath}`);
  }
  // GitHub gives the key in PKCS#1 format. The library needs PKCS#8, but on Node it converts the key itself,
  // so the .pem file from GitHub works as it is.
  const auth = createAppAuth({ appId: credentials.appId, privateKey });
  const authentication = await auth({ type: 'installation', installationId: credentials.installationId });
  return authentication.token;
}
