import { Component, inject, signal, ElementRef, ViewChild, AfterViewChecked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgClass, NgFor, NgIf } from '@angular/common';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatFormFieldModule } from '@angular/material/form-field';
import { CdkTextareaAutosize } from '@angular/cdk/text-field';
import { ChatService } from '../../../../core/services/chat.service';

@Component({
  selector: 'app-ai-chat',
  standalone: true,
  imports: [
    FormsModule, NgFor, NgIf, NgClass,
    MatInputModule, MatButtonModule, MatProgressSpinnerModule, MatFormFieldModule,
    CdkTextareaAutosize
  ],
  templateUrl: './ai-chat.component.html',
  styleUrl: './ai-chat.component.scss'
})
export class AiChatComponent implements AfterViewChecked {
  private chatService = inject(ChatService);
  @ViewChild('messagesContainer') private messagesContainer!: ElementRef;

  messages = this.chatService.messages;
  loading = this.chatService.loading;
  inputText = signal('');

  send(): void {
    const text = this.inputText().trim();
    if (!text || this.loading()) return;
    this.inputText.set('');
    this.chatService.send(text).subscribe();
  }

  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.send();
    }
  }

  ngAfterViewChecked(): void {
    if (this.messagesContainer) {
      const el = this.messagesContainer.nativeElement;
      el.scrollTop = el.scrollHeight;
    }
  }
}
