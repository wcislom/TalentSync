import { Simulators } from './simulators';

/** Runs one step and records its outcome on the page; a failed step throws and stops the scenario. */
export type Step = <T>(label: string, action: () => Promise<T>) => Promise<T>;

export interface Check {
  label: string;
  url(simulators: Simulators, candidateId: string): string;
  /** Narrows a list response to the scenario's candidate. */
  select?(body: unknown, candidateId: string): unknown;
}

export interface Scenario {
  /** Prefix of the candidate id; every run gets a new id, because simulator state is never reset. */
  key: string;
  title: string;
  description: string;
  expectation: string;
  run(simulators: Simulators, candidateId: string, step: Step): Promise<void>;
  checks: Check[];
}
