import { JsonPipe } from '@angular/common';
import { httpResource, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { SimulatorsClient } from '../data-access/simulators-client';
import { ResultCheck } from '../model/result-check';
import { Scenario } from '../model/scenario';

interface StepEntry {
  label: string;
  status: 'running' | 'done' | 'failed';
  error?: string;
}

@Component({
  selector: 'app-scenario-card',
  imports: [JsonPipe, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './scenario-card.html',
  styleUrl: './scenario-card.scss',
})
export class ScenarioCard {
  readonly scenario = input.required<Scenario>();

  private readonly simulators = inject(SimulatorsClient);

  protected readonly candidateId = signal<string | undefined>(undefined);
  protected readonly steps = signal<StepEntry[]>([]);
  protected readonly running = signal(false);

  private readonly check = signal<{ check: ResultCheck; url: string } | undefined>(undefined);
  protected readonly checkLabel = computed(() => this.check()?.check.label);
  protected readonly checkResponse = httpResource<unknown>(() => this.check()?.url);
  protected readonly checkResult = computed(() => {
    const current = this.check();
    const candidateId = this.candidateId();
    if (!current || !candidateId || !this.checkResponse.hasValue()) {
      return undefined;
    }
    const body = this.checkResponse.value();
    return current.check.select ? current.check.select(body, candidateId) : body;
  });
  protected readonly checkError = computed(() => describe(this.checkResponse.error()));

  async run(): Promise<void> {
    // A new id per run: simulator state lives until the containers restart, so a reused id would mix runs.
    const candidateId = `c-${this.scenario().key}-${crypto.randomUUID().slice(0, 8)}`;
    this.candidateId.set(candidateId);
    this.steps.set([]);
    this.check.set(undefined);
    this.running.set(true);
    try {
      await this.scenario().run(this.simulators, candidateId, (label, action) => this.step(label, action));
    } catch {
      // The failed step is already on the page; the remaining steps are skipped on purpose.
    } finally {
      this.running.set(false);
    }
  }

  runCheck(check: ResultCheck): void {
    const candidateId = this.candidateId();
    if (!candidateId) {
      return;
    }
    const url = check.url(this.simulators, candidateId);
    if (this.check()?.url === url) {
      this.checkResponse.reload();
    } else {
      this.check.set({ check, url });
    }
  }

  private async step<T>(label: string, action: () => Promise<T>): Promise<T> {
    const index = this.steps().length;
    this.steps.update((steps) => [...steps, { label, status: 'running' }]);
    try {
      const result = await action();
      this.updateStep(index, { status: 'done' });
      return result;
    } catch (error) {
      this.updateStep(index, { status: 'failed', error: describe(error) });
      throw error;
    }
  }

  private updateStep(index: number, changes: Partial<StepEntry>): void {
    this.steps.update((steps) => steps.map((step, i) => (i === index ? { ...step, ...changes } : step)));
  }
}

function describe(error: unknown): string | undefined {
  if (error === undefined) {
    return undefined;
  }
  if (error instanceof HttpErrorResponse) {
    // Status 0: the browser got no response it may read, so the simulator is down or CORS refused the origin.
    return error.status === 0
      ? `No response from ${error.url ?? 'the simulator'}: is it running, and does it allow this origin (CORS)?`
      : `${error.status} ${error.statusText}`;
  }
  return error instanceof Error ? error.message : String(error);
}
