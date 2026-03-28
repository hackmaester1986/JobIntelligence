import { Component, inject, OnInit, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DecimalPipe, NgIf } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { JobDetail } from '../../../core/models/job.model';
import { JobsService } from '../../../core/services/jobs.service';

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [NgIf, DecimalPipe, RouterLink, MatCardModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './job-detail.component.html',
  styleUrl: './job-detail.component.scss'
})
export class JobDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private jobsService = inject(JobsService);
  private location = inject(Location);

  job = signal<JobDetail | null>(null);
  loading = signal(true);

  goBack(): void { this.location.back(); }

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.jobsService.getJob(id).subscribe({
      next: j => { this.job.set(j); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }
}
