import { Component, computed, input } from '@angular/core';
import { NgIf } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { ChartData, ChartOptions } from 'chart.js';
import { SnapshotPoint } from '../../../../core/models/company.model';

@Component({
  selector: 'app-company-hiring-chart',
  standalone: true,
  imports: [BaseChartDirective, NgIf],
  template: `
    <div *ngIf="data().length === 0" class="empty">No data available for this period.</div>
    <canvas *ngIf="data().length > 0" baseChart
      [data]="chartData()"
      [options]="options"
      type="line"
      style="height:100%">
    </canvas>
  `,
  styles: [`.empty { color: rgba(0,0,0,.38); text-align: center; padding: 40px 0; font-size: 14px; }`]
})
export class CompanyHiringChartComponent {
  data  = input<SnapshotPoint[]>([]);
  chart = input<'active' | 'net'>('active');

  options: ChartOptions<'line'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: { y: { beginAtZero: false }, x: { ticks: { maxTicksLimit: 10 } } },
    elements: { line: { tension: 0.3 }, point: { radius: 2 } }
  };

  chartData = computed<ChartData<'line'>>(() => {
    const pts = this.data();
    const labels = pts.map(p => p.date.slice(0, 10));
    const isActive = this.chart() === 'active';
    const values = isActive
      ? pts.map(p => p.activeJobs)
      : pts.map(p => p.added - p.removed);
    const color = isActive ? 'rgba(63,81,181,1)' : 'rgba(0,188,212,1)';
    const fill  = isActive ? 'rgba(63,81,181,0.1)' : 'rgba(0,188,212,0.1)';

    return {
      labels,
      datasets: [{
        data: values,
        borderColor: color,
        backgroundColor: fill,
        fill: true,
        pointBackgroundColor: color,
      }]
    };
  });
}
