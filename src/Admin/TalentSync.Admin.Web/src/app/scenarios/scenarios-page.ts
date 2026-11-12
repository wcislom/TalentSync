import { Component, inject } from '@angular/core';
import { APP_CONFIG } from '../config';
import { ScenarioCard } from './scenario-card';
import { SCENARIOS } from './scenarios';

@Component({
  selector: 'app-scenarios-page',
  imports: [ScenarioCard],
  template: `
    <h1>Scenarios</h1>
    <p>
      Each run uses a new candidate id. A webhook a step triggers is one trace in the
      <a [href]="tracesUrl" target="_blank" rel="noopener">Aspire Dashboard</a>.
    </p>
    <div class="grid">
      @for (scenario of scenarios; track scenario.key) {
        <app-scenario-card [scenario]="scenario" />
      }
    </div>
  `,
  styles: `
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(480px, 1fr));
      gap: 16px;
    }
  `,
})
export class ScenariosPage {
  protected readonly scenarios = SCENARIOS;
  protected readonly tracesUrl = inject(APP_CONFIG).tracesUrl;
}
