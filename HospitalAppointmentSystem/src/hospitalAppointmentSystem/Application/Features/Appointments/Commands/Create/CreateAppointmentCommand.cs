using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Features.Appointments.Rules;
using Application.Services.Encryptions;
using Application.Services.Repositories;
using AutoMapper;
using Domain.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using MediatR;
using MimeKit;
using NArchitecture.Core.Security.Entities;

namespace Application.Features.Appointments.Commands.Create
{
    public class CreateAppointmentCommand : IRequest<CreatedAppointmentResponse>
    {
        public DateOnly Date { get; set; }
        public TimeOnly Time { get; set; }
        public bool Status { get; set; }
        public Guid DoctorID { get; set; }
        public Guid PatientID { get; set; }
    }

    public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, CreatedAppointmentResponse>
    {
        private readonly IMapper _mapper;
        private readonly IAppointmentRepository _appointmentRepository;
        private readonly IDoctorRepository _doctorRepository;
        private readonly IPatientRepository _patientRepository;
        private readonly IBranchRepository _branchRepository;
        private readonly AppointmentBusinessRules _appointmentBusinessRules;

        public CreateAppointmentCommandHandler(IMapper mapper, IAppointmentRepository appointmentRepository, IDoctorRepository doctorRepository, IPatientRepository patientRepository, IBranchRepository branchRepository, AppointmentBusinessRules appointmentBusinessRules)
        {
            _mapper = mapper;
            _appointmentRepository = appointmentRepository;
            _doctorRepository = doctorRepository;
            _patientRepository = patientRepository;
            _branchRepository = branchRepository;
            _appointmentBusinessRules = appointmentBusinessRules;
        }

        public async Task<CreatedAppointmentResponse> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
        {

            // Yeni randevu olu�tur
            Appointment appointment = _mapper.Map<Appointment>(request);

            // Doctor bilgisini al
            Doctor doctor = await _doctorRepository.GetAsync(d => d.Id == request.DoctorID);
            appointment.Doctor = doctor;

            // Patient bilgisini al
            Patient patient = await _patientRepository.GetAsync(p => p.Id == request.PatientID);
            appointment.Patient = patient;

            // Bran� bilgisini al
            Branch branch = await _branchRepository.GetAsync(p => p.Id == doctor.BranchID);
            doctor.Branch = branch;

            // Hasta ayn� doktordan ayn� g�ne ait randevusu olup olmad���n� kontrol et
            await _appointmentBusinessRules.PatientCannotHaveMultipleAppointmentsOnSameDayWithSameDoctor(request.PatientID, request.DoctorID, request.Date);

            // Ayn� doktor ve tarihte silinmi� randevu var m� kontrol et
            Appointment existingDeletedAppointment = await _appointmentRepository.GetAsync(a =>
                a.PatientID == request.PatientID &&
                a.DoctorID == request.DoctorID &&
                a.Date == request.Date &&
                a.DeletedDate != null);

            if (existingDeletedAppointment != null)
            {
                // Silinmi� randevuyu g�ncelle
                existingDeletedAppointment.Time = request.Time;
                existingDeletedAppointment.Status = request.Status;
                existingDeletedAppointment.DeletedDate = null; // Silinmi� durumu kald�r
                await _appointmentRepository.UpdateAsync(existingDeletedAppointment);



                await SendAppointmentConfirmationMail(existingDeletedAppointment);
                CreatedAppointmentResponse response = _mapper.Map<CreatedAppointmentResponse>(existingDeletedAppointment);
                return response;
            }
            else
            {
             

                await _appointmentRepository.AddAsync(appointment);

                // Olu�turulan randevu bilgilerini mail olarak g�nder
                await SendAppointmentConfirmationMail(appointment);

                CreatedAppointmentResponse response = _mapper.Map<CreatedAppointmentResponse>(appointment);
                return response;
            }
        }

        private async Task SendAppointmentConfirmationMail(Appointment appointment)
        {
            // Mail i�eri�ini haz�rla
            var mailMessage = new MimeMessage();
            mailMessage.From.Add(new MailboxAddress("Pair 5 Hastanesi", "fatmabireltr@gmail.com")); // G�nderen bilgisi
            appointment.Patient.Email = CryptoHelper.Decrypt(appointment.Patient.Email);
            appointment.Patient.FirstName = CryptoHelper.Decrypt(appointment.Patient.FirstName);
            appointment.Patient.LastName = CryptoHelper.Decrypt(appointment.Patient.LastName);
            appointment.Doctor.FirstName = CryptoHelper.Decrypt(appointment.Doctor.FirstName);
            appointment.Doctor.LastName = CryptoHelper.Decrypt(appointment.Doctor.LastName);

            mailMessage.To.Add(new MailboxAddress("Pair 5 Hastanesi", appointment.Patient.Email)); // Al�c� bilgisi 
            mailMessage.Subject = "Randevu Bilgilendirme"; // Mail konusu

            // HTML ve CSS i�eri�i olu�tur
            var bodyBuilder = new BodyBuilder();
            bodyBuilder.HtmlBody = $@"
       <html>
        <head>
            <style>
                body {{ font-family: Arial, sans-serif; }}
                .container {{ border: 1px solid red; padding: 10px; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <p>Say�n {appointment.Patient.FirstName} {appointment.Patient.LastName},</p>
                <p>{appointment.Date} tarihinde, saat {appointment.Time} i�in bir randevu ald�n�z.</p>
                <p>Doktor: {appointment.Doctor.Title} {appointment.Doctor.FirstName} {appointment.Doctor.LastName}</p>
                <p>Bran�: {appointment.Doctor.Branch.Name}</p>
            </div>
        </body>
        </html>";

            // MimeKit'e g�vdeyi ayarla
            mailMessage.Body = bodyBuilder.ToMessageBody();

            // SMTP ile ba�lant� kur ve maili g�nder
            using (var smtp = new SmtpClient())
            {
                smtp.Connect("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                smtp.Authenticate("fatmabireltr@gmail.com", "rxuv hpfv wlqq htpa");
                await smtp.SendAsync(mailMessage);
                smtp.Disconnect(true);
            }
        }
    }
}
