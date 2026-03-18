import { Component, input, computed } from '@angular/core';
import { BaseChartDirective } from 'ng2-charts';
import { ChartData, ChartOptions } from 'chart.js';
import { SeniorityBucket } from '../../../../core/models/stats.model';

const COLORS = [
  '#3F51B5', '#E91E63', '#00BCD4', '#FF9800', '#4CAF50',
  '#9C27B0', '#F44336', '#2196F3', '#FFEB3B', '#795548'
];

@Component({
  selector: 'app-seniority-chart',
  standalone: true,
  imports: [BaseChartDirective],
  template: `
    <canvas baseChart
      [data]="chartData()"
      [options]="options"
      type="doughnut">
    </canvas>
  `
})
export class SeniorityChartComponent {
  data = input<SeniorityBucket[]>([]);

  options: ChartOptions<'doughnut'> = {
    responsive: true,
    plugins: { legend: { position: 'right' } }
  };

  chartData = computed<ChartData<'doughnut'>>(() => ({
    labels: this.data().map(b => b.label),
    datasets: [{
      data: this.data().map(b => b.count),
      backgroundColor: this.data().map((_, i) => COLORS[i % COLORS.length])
    }]
  }));
}
