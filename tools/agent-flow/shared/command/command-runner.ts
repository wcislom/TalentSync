import { format, logger } from '../logging/logger.ts';

// Sets the exit code instead of calling process.exit(), so pending output (and the orchestrator's lock release)
// still finish.
export function failProcess(): void {
  process.exitCode = 1;
}

// The entry point of a command run through npm: an error becomes one log line and exit code 1.
export function runCommand(commandName: string, main: () => Promise<void>): void {
  main().catch((error: unknown) => {
    logger.error(`${commandName} failed: ${format.errorMessage(error)}`);
    failProcess();
  });
}
