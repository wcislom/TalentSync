import { format, logger } from '../logging/logger.ts';

// Marks the process as failed (exit code 1) without stopping it at once. process.exit() would stop it
// immediately, and log lines not yet written, or the orchestrator's lock release, would be lost.
export function failProcess(): void {
  process.exitCode = 1;
}

// Every npm command (prepare-branch, publish-pr, ...) starts through this function. If the command throws,
// the user sees one clear log line instead of a stack trace, and the exit code is 1.
export function runCommand(commandName: string, main: () => Promise<void>): void {
  main().catch((error: unknown) => {
    logger.error(`${commandName} failed: ${format.errorMessage(error)}`);
    failProcess();
  });
}
