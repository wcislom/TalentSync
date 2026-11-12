import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import { RUNTIME_CONFIG } from '../../core/runtime-config/runtime-config';

/** A job the ATS simulator seeds on start (simulators/FakeTeamtailor). */
export const SEEDED_JOB_ID = 'job-1';

export interface NewCandidate {
  firstName: string;
  lastName: string;
  email: string;
  phone?: string;
  resumeText: string;
}

export interface AnomalyRule {
  mode: string;
  candidateId: string;
  seconds: number;
}

interface WebhookDelivery {
  eventName: string;
  /** Null when Ingress did not answer. */
  statusCode: number | null;
}

interface StateChange {
  webhooks: WebhookDelivery[];
}

/** Drives the three simulators. Responses stay untyped except the ids a later step needs and webhook delivery. */
@Injectable({ providedIn: 'root' })
export class SimulatorsClient {
  private readonly http = inject(HttpClient);
  private readonly config = inject(RUNTIME_CONFIG);

  apply(jobId: string, candidateId: string, candidate: NewCandidate): Promise<{ jobApplicationId: string }> {
    return this.change(
      this.http.post<StateChange & { jobApplicationId: string }>(`${this.ats}/recruiter/jobs/${jobId}/apply`, {
        candidateId,
        ...candidate,
      }),
    );
  }

  changeStage(jobApplicationId: string, stage: string): Promise<unknown> {
    return this.change(
      this.http.patch<StateChange>(`${this.ats}/recruiter/job-applications/${jobApplicationId}/stage`, { stage }),
    );
  }

  updateCandidate(candidateId: string, changes: Partial<NewCandidate>): Promise<unknown> {
    return this.change(this.http.patch<StateChange>(`${this.ats}/recruiter/candidates/${candidateId}`, changes));
  }

  breakAts(rule: AnomalyRule): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.ats}/anomalies`, rule));
  }

  breakCrm(rule: AnomalyRule): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.config.crmSimulatorUrl}/anomalies`, rule));
  }

  atsCallsUrl(candidateId: string): string {
    return `${this.ats}/calls?candidateId=${encodeURIComponent(candidateId)}`;
  }

  crmCallsUrl(candidateId: string): string {
    return `${this.config.crmSimulatorUrl}/calls?candidateId=${encodeURIComponent(candidateId)}`;
  }

  crmContactsUrl(): string {
    return `${this.config.crmSimulatorUrl}/contacts`;
  }

  profileUrl(candidateId: string): string {
    return `${this.config.matchingPlatformSimulatorUrl}/profiles/${encodeURIComponent(candidateId)}`;
  }

  private get ats(): string {
    return this.config.atsSimulatorUrl;
  }

  // The simulator applies a change even when its webhook fails (Teamtailor does not retry a delivery), so its 2xx
  // says nothing about whether TalentSync will ever see the event. Each delivery is checked on its own.
  private async change<T extends StateChange>(request: Observable<T>): Promise<T> {
    const response = await firstValueFrom(request);
    const failed = response.webhooks.filter((w) => w.statusCode === null || w.statusCode < 200 || w.statusCode > 299);
    if (failed.length > 0) {
      const details = failed
        .map((w) => `${w.eventName}: ${w.statusCode ?? 'no response, is Ingress.Api running?'}`)
        .join('; ');
      throw new Error(`The change was made, but its webhook did not reach Ingress (${details})`);
    }
    return response;
  }
}
